using Bandroom.Api.Data;
using Bandroom.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Jobs;

/// <summary>
/// "We play thursdays": for every band with a standing practice day, make
/// sure the next two occurrences exist as PROPOSED practices. Proposed, not
/// confirmed — the rsvp round is still what makes a practice real (spec
/// principle 2). A day that already holds a practice (any status but cancelled)
/// is left alone, so cancelling one week doesn't summon it back.
/// Runs daily; no band context, so every query opts out of the filters.
/// </summary>
public sealed class RecurringPracticeJob(
    AppDbContext db, IClock clock, Features.Push.IPushSender push, Features.Auth.AuthOptions authOptions, ILogger<RecurringPracticeJob> logger)
{
    public const int WeeksAhead = 2;

    public Task RunAsync(CancellationToken ct) => RunAtAsync(clock.GetCurrentInstant(), ct);

    public async Task<int> RunAtAsync(Instant now, CancellationToken ct)
    {
        var bands = await db.Bands.Where(b => b.RecurringPracticeDay != null).ToListAsync(ct);
        var created = 0;
        foreach (var band in bands)
        {
            var organiser = await db.Memberships.IgnoreQueryFilters()
                .Where(m => m.BandId == band.Id && m.LeftAt == null)
                .OrderByDescending(m => m.Role)
                .ThenBy(m => m.JoinedAt)
                .Select(m => (Guid?)m.Id)
                .FirstOrDefaultAsync(ct);
            if (organiser is null)
            {
                continue;
            }

            var today = BandTime.TodayIn(band, now);
            var next = today.Next(band.RecurringPracticeDay!.Value);
            var horizon = today.PlusDays(7 * WeeksAhead);
            for (var date = next; date <= horizon; date = date.PlusWeeks(1))
            {
                var occupied = await db.Events.IgnoreQueryFilters().AnyAsync(
                    e => e.BandId == band.Id && e.Type == EventType.Practice && e.Date == date, ct);
                if (occupied)
                {
                    continue;
                }

                var evt = new Event
                {
                    BandId = band.Id,
                    Type = EventType.Practice,
                    Status = EventStatus.Proposed,
                    Date = date,
                    Slot = band.DefaultPracticeSlot,
                    CreatedByMembershipId = organiser.Value,
                    CreatedAt = now,
                };
                db.Events.Add(evt);
                await db.SaveChangesAsync(ct);
                // Everyone gets the one-tap answers — the standing day is nobody's proposal.
                await Features.Events.EventPush.ProposedAsync(db, push, authOptions, band, evt, null, now, ct);
                created++;
            }
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("proposed {Count} standing practice(s)", created);
        }

        return created;
    }
}
