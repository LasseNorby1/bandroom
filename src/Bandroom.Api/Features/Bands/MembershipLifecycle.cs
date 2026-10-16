using Bandroom.Api.Data;
using Bandroom.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Bands;

/// <summary>
/// What "leaving a band" means, in one place — used by leave, remove and
/// account deletion. Runs with the filters off and explicit band ids, because
/// account deletion has no band context.
/// </summary>
public static class MembershipLifecycle
{
    public static async Task LeaveAsync(AppDbContext db, Membership membership, Instant now, CancellationToken ct)
    {
        if (membership.LeftAt is not null)
        {
            return;
        }

        var band = await db.Bands.SingleAsync(b => b.Id == membership.BandId, ct);
        membership.LeftAt = now;

        // Their answers on upcoming events stop counting; past ones are history.
        var today = BandTime.TodayIn(band, now);
        await db.Rsvps
            .IgnoreQueryFilters()
            .Where(r => r.BandId == band.Id && r.MembershipId == membership.Id &&
                        db.Events.IgnoreQueryFilters().Any(e => e.Id == r.EventId && e.Date >= today))
            .ExecuteDeleteAsync(ct);

        var remaining = await db.Memberships
            .IgnoreQueryFilters()
            .Where(m => m.BandId == band.Id && m.Id != membership.Id && m.LeftAt == null)
            .OrderBy(m => m.JoinedAt)
            .ToListAsync(ct);

        if (remaining.Count == 0)
        {
            // Nobody left in the room: the band goes with its last member.
            // (Storage objects become orphans for a later sweep.)
            db.Bands.Remove(band);
            return;
        }

        if (remaining.All(m => m.Role != BandRole.Admin))
        {
            // A band without an admin can't change settings or invite anyone —
            // the longest-standing member takes over.
            remaining[0].Role = BandRole.Admin;
        }
    }
}
