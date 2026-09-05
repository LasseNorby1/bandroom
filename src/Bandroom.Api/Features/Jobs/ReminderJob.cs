using Bandroom.Api.Data;
using Bandroom.Api.Features.Events;
using Bandroom.Api.Infrastructure;
using Bandroom.Api.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Jobs;

/// <summary>
/// Day-before nudge (spec §4): runs hourly; a confirmed event gets exactly one
/// reminder, sent once the band-local clock passes 17:00 on the day before.
/// Jobs have no band context, so every query opts out of the filters explicitly.
/// </summary>
public sealed class ReminderJob(AppDbContext db, IAppEmailSender email, IClock clock, ILogger<ReminderJob> logger)
{
    public Task RunAsync(CancellationToken ct) => RunAtAsync(clock.GetCurrentInstant(), ct);

    public async Task RunAtAsync(Instant now, CancellationToken ct)
    {
        // Reminders go out for band-local "tomorrow", which is never earlier than
        // yesterday in utc — so anything older can't need one. Without this floor
        // every event that was confirmed too late for its reminder would be
        // reloaded on every hourly run, forever.
        var floor = now.InUtc().Date.PlusDays(-1);
        var pending = await db.Events
            .IgnoreQueryFilters()
            .Where(e => e.Status == EventStatus.Confirmed && e.ReminderSentAt == null && e.Date >= floor)
            .ToListAsync(ct);
        if (pending.Count == 0)
        {
            return;
        }

        var bandIds = pending.Select(e => e.BandId).Distinct().ToList();
        var bands = await db.Bands
            .Where(b => bandIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, ct);
        var recipients = await db.Memberships
            .IgnoreQueryFilters()
            .Where(m => bandIds.Contains(m.BandId))
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.BandId, u.Email, u.DisplayName })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var evt in pending)
        {
            var band = bands[evt.BandId];
            var local = now.InZone(BandTime.ZoneOf(band));
            if (evt.Date != local.Date.PlusDays(1) || local.Hour < 17)
            {
                continue;
            }

            var (start, _) = SlotTimes.Resolve(evt);
            var summary = EventText.Summarize(evt, band);
            var location = evt.Location ?? band.RehearsalSpace;
            var body = $"Tomorrow at {start}: {summary}." + (location is null ? "" : $" Location: {location}.");

            foreach (var recipient in recipients.Where(r => r.BandId == evt.BandId))
            {
                await email.SendAsync(recipient.Email!, $"Tomorrow: {summary}", body, ct);
            }

            evt.ReminderSentAt = now;
            sent++;
        }

        if (sent > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("sent reminders for {Count} event(s)", sent);
        }
    }
}
