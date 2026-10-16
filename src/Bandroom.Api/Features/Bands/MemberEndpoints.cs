using Bandroom.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Bands;

public static class MemberEndpoints
{
    public static IEndpointRouteBuilder MapMemberEndpoints(this IEndpointRouteBuilder routes)
    {
        var members = routes.MapGroup("/bands/{bandId:guid}/members")
            .RequireAuthorization()
            .WithTags("bands")
            .AddEndpointFilter<BandMemberFilter>();

        members.MapDelete("/me", LeaveAsync).WithSummary("Leave the band");
        members.MapDelete("/{membershipId:guid}", RemoveAsync)
            .AddEndpointFilter<RequireBandAdminFilter>()
            .WithSummary("Remove a member (admin)");
        members.MapPatch("/{membershipId:guid}", UpdateRoleAsync)
            .AddEndpointFilter<RequireBandAdminFilter>()
            .WithSummary("Change a member's role (admin)");

        return routes;
    }

    private static async Task<NoContent> LeaveAsync(
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        var membership = await db.Memberships.SingleAsync(m => m.Id == bandContext.MembershipId, ct);
        await MembershipLifecycle.LeaveAsync(db, membership, clock.GetCurrentInstant(), ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> RemoveAsync(
        Guid membershipId,
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        if (membershipId == bandContext.MembershipId)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["membershipId"] = ["Use 'leave' to remove yourself."],
            });
        }

        var membership = await db.Memberships
            .SingleOrDefaultAsync(m => m.Id == membershipId && m.LeftAt == null, ct);
        if (membership is null)
        {
            return TypedResults.NotFound();
        }

        await MembershipLifecycle.LeaveAsync(db, membership, clock.GetCurrentInstant(), ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<MemberResponse>, NotFound, ValidationProblem>> UpdateRoleAsync(
        Guid membershipId,
        UpdateMemberRequest request,
        AppDbContext db,
        CancellationToken ct)
    {
        var membership = await db.Memberships
            .SingleOrDefaultAsync(m => m.Id == membershipId && m.LeftAt == null, ct);
        if (membership is null)
        {
            return TypedResults.NotFound();
        }

        if (request.Role == BandRole.Member && membership.Role == BandRole.Admin)
        {
            var otherAdmins = await db.Memberships
                .CountAsync(m => m.Id != membershipId && m.LeftAt == null && m.Role == BandRole.Admin, ct);
            if (otherAdmins == 0)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["role"] = ["The band needs at least one admin — make someone else admin first."],
                });
            }
        }

        membership.Role = request.Role;
        await db.SaveChangesAsync(ct);

        var displayName = await db.Users.Where(u => u.Id == membership.UserId).Select(u => u.DisplayName).SingleAsync(ct);
        return TypedResults.Ok(new MemberResponse(
            membership.Id, membership.UserId, displayName, membership.Role, membership.Instrument, membership.JoinedAt));
    }
}
