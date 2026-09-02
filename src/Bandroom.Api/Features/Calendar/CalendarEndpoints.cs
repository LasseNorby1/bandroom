using System.Text;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Events;
using Bandroom.Api.Infrastructure;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Calendar;

public static class CalendarEndpoints
{
    public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder routes)
    {
        // A capability url: the token IS the credential. Read-only calendar data,
        // per member, per band; personal calendar apps subscribe to it (spec §5.1).
        routes.MapGet("/calendar/{icsToken}.ics", GetFeedAsync)
            .AllowAnonymous()
            .WithTags("calendar")
            .WithSummary("Personal ics feed for one membership");

        return routes;
    }

    private static async Task<Results<ContentHttpResult, NotFound>> GetFeedAsync(
        string icsToken,
        AppDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        var membership = await db.Memberships
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(m => m.IcsToken == icsToken, ct);
        if (membership is null)
        {
            return TypedResults.NotFound();
        }

        var band = await db.Bands.SingleAsync(b => b.Id == membership.BandId, ct);
        var zone = BandTime.ZoneOf(band);
        var today = clock.GetCurrentInstant().InZone(zone).Date;

        var events = await db.Events
            .IgnoreQueryFilters()
            .Where(e => e.BandId == band.Id &&
                        e.Status != EventStatus.Cancelled &&
                        e.Date >= today.PlusDays(-30) &&
                        e.Date <= today.PlusDays(180))
            .OrderBy(e => e.Date)
            .ToListAsync(ct);

        var calendar = new Ical.Net.Calendar();
        calendar.AddProperty("X-WR-CALNAME", $"{band.Name} — Bandroom");

        foreach (var evt in events)
        {
            var (start, end) = SlotTimes.Resolve(evt);
            // Band-local wall time → UTC through the band's zone: a 19:00 practice
            // stays 19:00 across DST (AtLeniently shifts through the spring gap).
            var startUtc = zone.AtLeniently(evt.Date.At(start)).ToDateTimeUtc();
            var endUtc = zone.AtLeniently(evt.Date.At(end)).ToDateTimeUtc();

            calendar.Events.Add(new CalendarEvent
            {
                Uid = $"{evt.Id}@bandroom",
                Summary = EventText.Summarize(evt, band),
                Location = evt.Location ?? (evt.Type == EventType.Practice ? band.RehearsalSpace : null),
                Start = new CalDateTime(startUtc),
                End = new CalDateTime(endUtc),
                Status = evt.Status == EventStatus.Proposed ? "TENTATIVE" : "CONFIRMED",
            });
        }

        var ics = new CalendarSerializer().SerializeToString(calendar);
        return TypedResults.Text(ics, "text/calendar", Encoding.UTF8);
    }
}
