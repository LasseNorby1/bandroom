using Bandroom.Api.Data;
using Bandroom.Api.Features.Events;
using Bandroom.Api.Infrastructure;
using Bandroom.Domain.Scheduling;
using Ical.Net;
using Ical.Net.DataTypes;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Availability;

/// <summary>
/// Reads one member's private calendar feed (Google "secret address in iCal
/// format", Apple's public calendar link, Outlook's published ics) and turns
/// the next 60 days of events into busy slots. Opt-in per member, no OAuth,
/// nothing written back. Shared by the save endpoint (immediate feedback) and
/// the refresh job; the job has no band context, so filters are off and band
/// ids explicit throughout.
/// </summary>
public sealed class CalendarImporter(AppDbContext db, IHttpClientFactory httpClientFactory, IClock clock)
{
    public const int DaysAhead = 60;

    public const int MaxFeedBytes = 2 * 1024 * 1024;

    public async Task<string?> ImportAsync(Membership membership, Band band, CancellationToken ct)
    {
        string? error = null;
        var busy = new List<ImportedBusyDay>();
        try
        {
            var http = httpClientFactory.CreateClient("calendar");
            using var response = await http.GetAsync(membership.CalendarImportUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxFeedBytes)
            {
                throw new InvalidOperationException("The feed is larger than 2 MB.");
            }

            var ics = await response.Content.ReadAsStringAsync(ct);
            busy = Extract(ics, band, BandTime.TodayIn(band, clock.GetCurrentInstant()))
                .Select(b => new ImportedBusyDay
                {
                    BandId = band.Id,
                    MembershipId = membership.Id,
                    Date = b.Date,
                    Slot = b.Slot,
                    Summary = b.Summary,
                })
                .ToList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The feed is untrusted input: anything the fetch or the parser throws
            // (Ical.Net has several of its own) is "not a calendar", never a 500.
            error = ex is HttpRequestException or TaskCanceledException or InvalidOperationException
                ? (ex.Message.Length > 200 ? ex.Message[..200] : ex.Message)
                : "That address doesn't serve an ics calendar.";
        }

        if (error is null)
        {
            await db.ImportedBusyDays.IgnoreQueryFilters()
                .Where(b => b.MembershipId == membership.Id)
                .ExecuteDeleteAsync(ct);
            db.ImportedBusyDays.AddRange(busy);
            membership.CalendarImportedAt = clock.GetCurrentInstant();
        }

        membership.CalendarImportError = error;
        await db.SaveChangesAsync(ct);
        return error;
    }

    public sealed record BusySlot(LocalDate Date, PracticeSlot? Slot, string? Summary);

    /// <summary>Pure: ics text → busy (date, slot) pairs in the band's zone, deduplicated.</summary>
    public static IReadOnlyList<BusySlot> Extract(string ics, Band band, LocalDate from)
    {
        var zone = BandTime.ZoneOf(band);
        var to = from.PlusDays(DaysAhead);
        var calendar = Ical.Net.Calendar.Load(ics) ?? throw new FormatException("Not an ics calendar.");
        var windowStart = zone.AtStartOfDay(from).ToDateTimeUtc();
        var windowEnd = zone.AtStartOfDay(to.PlusDays(1)).ToDateTimeUtc();

        var found = new Dictionary<(LocalDate, PracticeSlot?), string?>();
        // Ical.Net 5 streams occurrences lazily (recurring events never end); stop at the window's edge.
        foreach (var occurrence in calendar.GetOccurrences(new CalDateTime(windowStart))
                     .TakeWhile(o => o.Period.StartTime.AsUtc < windowEnd))
        {
            var summary = (occurrence.Source as Ical.Net.CalendarComponents.CalendarEvent)?.Summary;
            summary = summary is { Length: > 120 } ? summary[..120] : summary;
            var start = occurrence.Period.StartTime;
            var end = occurrence.Period.EndTime ?? start;

            if (!start.HasTime)
            {
                // All-day: every date from start to end (exclusive), whole day.
                var day = LocalDate.FromDateTime(start.Value);
                var last = LocalDate.FromDateTime(end.Value);
                for (var d = day; d < last || d == day; d = d.PlusDays(1))
                {
                    if (d >= from && d <= to)
                    {
                        found.TryAdd((d, null), summary);
                    }
                }

                continue;
            }

            var startLocal = Instant.FromDateTimeUtc(DateTime.SpecifyKind(start.AsUtc, DateTimeKind.Utc)).InZone(zone).LocalDateTime;
            var endLocal = Instant.FromDateTimeUtc(DateTime.SpecifyKind(end.AsUtc, DateTimeKind.Utc)).InZone(zone).LocalDateTime;
            for (var d = startLocal.Date; d <= endLocal.Date && d <= to; d = d.PlusDays(1))
            {
                if (d < from)
                {
                    continue;
                }

                foreach (var slot in new[] { PracticeSlot.Afternoon, PracticeSlot.Evening })
                {
                    var (slotStart, slotEnd) = slot == PracticeSlot.Afternoon
                        ? (SlotTimes.AfternoonStart, SlotTimes.AfternoonEnd)
                        : (SlotTimes.EveningStart, SlotTimes.EveningEnd);
                    var slotFrom = d.At(slotStart);
                    var slotTo = d.At(slotEnd);
                    if (startLocal < slotTo && endLocal > slotFrom)
                    {
                        found.TryAdd((d, slot), summary);
                    }
                }
            }
        }

        return found.Select(kv => new BusySlot(kv.Key.Item1, kv.Key.Item2, kv.Value)).OrderBy(b => b.Date).ToList();
    }
}

/// <summary>Refreshes every member's calendar overlay; runs every six hours.</summary>
public sealed class CalendarImportJob(AppDbContext db, CalendarImporter importer, ILogger<CalendarImportJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var members = await db.Memberships.IgnoreQueryFilters()
            .Where(m => m.CalendarImportUrl != null && m.LeftAt == null)
            .Join(db.Bands, m => m.BandId, b => b.Id, (m, b) => new { m, b })
            .ToListAsync(ct);
        var failures = 0;
        foreach (var x in members)
        {
            if (await importer.ImportAsync(x.m, x.b, ct) is not null)
            {
                failures++;
            }
        }

        logger.LogInformation("calendar import: {Count} feed(s), {Failures} failed", members.Count, failures);
    }
}
