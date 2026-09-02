using System.Text;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Events;
using Bandroom.Api.Infrastructure;
using Bandroom.Api.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Jobs;

/// <summary>
/// The weekly "anything change in the next two weeks?" nudge (spec §4) — the
/// staleness defence for the availability model, plus a two-week agenda.
/// </summary>
public sealed class DigestJob(AppDbContext db, IAppEmailSender email, IClock clock)
{
    public Task RunAsync(CancellationToken ct) => RunAtAsync(clock.GetCurrentInstant(), ct);

    public async Task RunAtAsync(Instant now, CancellationToken ct)
    {
        var bands = await db.Bands.ToListAsync(ct);
        foreach (var band in bands)
        {
            var today = BandTime.TodayIn(band, now);
            var events = await db.Events
                .IgnoreQueryFilters()
                .Where(e => e.BandId == band.Id &&
                            e.Status != EventStatus.Cancelled &&
                            e.Date >= today &&
                            e.Date <= today.PlusDays(14))
                .OrderBy(e => e.Date)
                .ToListAsync(ct);
            var members = await db.Memberships
                .IgnoreQueryFilters()
                .Where(m => m.BandId == band.Id)
                .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { u.Email, u.DisplayName })
                .ToListAsync(ct);

            var body = new StringBuilder();
            if (events.Count > 0)
            {
                body.AppendLine("The next two weeks:");
                foreach (var evt in events)
                {
                    var (start, _) = SlotTimes.Resolve(evt);
                    body.AppendLine($"- {evt.Date} {start}: {EventText.Summarize(evt, band)} ({evt.Status})");
                }
            }
            else
            {
                body.AppendLine("Nothing scheduled in the next two weeks.");
            }

            body.AppendLine();
            body.AppendLine("Anything change in your availability? Update your weekly pattern or add a blockout in Bandroom.");

            foreach (var member in members)
            {
                await email.SendAsync(member.Email!, $"Bandroom digest — {band.Name}", body.ToString(), ct);
            }
        }
    }
}
