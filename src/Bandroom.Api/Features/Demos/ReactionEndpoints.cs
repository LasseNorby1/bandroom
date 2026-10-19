using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Features.Events;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Demos;

public sealed record FireResponse(int FireCount, bool FiredByMe);

/// <summary>🔥 on a version. Put to light it, delete to take it back.</summary>
public static class ReactionEndpoints
{
    public static IEndpointRouteBuilder MapReactionEndpoints(this IEndpointRouteBuilder routes)
    {
        var versions = routes.MapGroup("/bands/{bandId:guid}/versions/{versionId:guid}/fire")
            .RequireAuthorization()
            .WithTags("demos")
            .AddEndpointFilter<BandMemberFilter>();

        versions.MapPut("/", FireAsync).WithSummary("🔥 this version");
        versions.MapDelete("/", UnfireAsync).WithSummary("Take the 🔥 back");

        return routes;
    }

    private static async Task<Results<Ok<FireResponse>, NotFound>> FireAsync(
        Guid versionId,
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        BandNotifier notifier,
        CancellationToken ct)
    {
        var version = await db.DemoVersions
            .Where(v => v.Id == versionId && v.Status == VersionStatus.Ready)
            .Select(v => new { v.SongId })
            .SingleOrDefaultAsync(ct);
        if (version is null)
        {
            return TypedResults.NotFound();
        }

        var me = bandContext.MembershipId!.Value;
        if (!await db.Reactions.AnyAsync(r => r.VersionId == versionId && r.MembershipId == me, ct))
        {
            db.Reactions.Add(new Reaction
            {
                BandId = bandContext.BandId!.Value,
                VersionId = versionId,
                MembershipId = me,
                CreatedAt = clock.GetCurrentInstant(),
            });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Double tap raced the unique index — already lit.
            }

            await notifier.DemoChangedAsync(bandContext.BandId!.Value, version.SongId, "fire", ct);
        }

        return TypedResults.Ok(await CountAsync(db, versionId, me, ct));
    }

    private static async Task<Results<Ok<FireResponse>, NotFound>> UnfireAsync(
        Guid versionId,
        BandContext bandContext,
        AppDbContext db,
        BandNotifier notifier,
        CancellationToken ct)
    {
        var ideaId = await db.DemoVersions.Where(v => v.Id == versionId).Select(v => (Guid?)v.SongId).SingleOrDefaultAsync(ct);
        if (ideaId is null)
        {
            return TypedResults.NotFound();
        }

        var me = bandContext.MembershipId!.Value;
        var removed = await db.Reactions.Where(r => r.VersionId == versionId && r.MembershipId == me).ExecuteDeleteAsync(ct);
        if (removed > 0)
        {
            await notifier.DemoChangedAsync(bandContext.BandId!.Value, ideaId.Value, "unfire", ct);
        }

        return TypedResults.Ok(await CountAsync(db, versionId, me, ct));
    }

    private static async Task<FireResponse> CountAsync(AppDbContext db, Guid versionId, Guid me, CancellationToken ct)
    {
        var count = await db.Reactions.CountAsync(r => r.VersionId == versionId, ct);
        var mine = await db.Reactions.AnyAsync(r => r.VersionId == versionId && r.MembershipId == me, ct);
        return new FireResponse(count, mine);
    }
}
