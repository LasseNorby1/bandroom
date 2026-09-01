using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using Bandroom.Api.Data;
using Bandroom.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using NodaTime;

namespace Bandroom.Api.Features.Bands;

public static class InviteEndpoints
{
    public static IEndpointRouteBuilder MapInviteEndpoints(this IEndpointRouteBuilder routes)
    {
        var invites = routes.MapGroup("/bands/{bandId:guid}/invites")
            .RequireAuthorization()
            .WithTags("invites")
            .AddEndpointFilter<BandMemberFilter>()
            .AddEndpointFilter<RequireBandAdminFilter>();

        invites.MapPost("/", CreateAsync).WithSummary("Create an invite link (raw token shown once)");
        invites.MapGet("/", ListAsync).WithSummary("Active invites");
        invites.MapDelete("/{inviteId:guid}", RevokeAsync).WithSummary("Revoke an invite");

        // Acceptance is NOT band-scoped — the caller isn't a member yet.
        routes.MapPost("/invites/{token}/accept", AcceptAsync)
            .RequireAuthorization()
            .WithTags("invites")
            .WithSummary("Join a band via invite token");

        return routes;
    }

    private static async Task<Results<Ok<InviteCreatedResponse>, ValidationProblem>> CreateAsync(
        CreateInviteRequest? request,
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        if (request?.MaxUses is < 1)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["maxUses"] = ["Max uses must be at least 1."],
            });
        }

        if (request?.ExpiresInDays is < 1 or > 365)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["expiresInDays"] = ["Expiry must be between 1 and 365 days."],
            });
        }

        var now = clock.GetCurrentInstant();
        var rawToken = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var invite = new BandInvite
        {
            BandId = bandContext.BandId!.Value,
            TokenHash = TokenHashing.Sha256Hex(rawToken),
            CreatedByMembershipId = bandContext.MembershipId!.Value,
            CreatedAt = now,
            ExpiresAt = now.Plus(Duration.FromDays(request?.ExpiresInDays ?? 30)),
            MaxUses = request?.MaxUses,
        };

        db.BandInvites.Add(invite);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new InviteCreatedResponse(invite.Id, rawToken, invite.ExpiresAt, invite.MaxUses));
    }

    private static async Task<Ok<List<InviteResponse>>> ListAsync(
        AppDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        var now = clock.GetCurrentInstant();

        // Band scoping comes from the global query filter.
        var invites = await db.BandInvites
            .Where(i => i.RevokedAt == null && i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new InviteResponse(i.Id, i.CreatedAt, i.ExpiresAt, i.UseCount, i.MaxUses))
            .ToListAsync(ct);
        return TypedResults.Ok(invites);
    }

    private static async Task<Results<NoContent, NotFound>> RevokeAsync(
        Guid inviteId,
        AppDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        // ExecuteUpdate flows through the query filter too — cross-band revoke is impossible.
        var revoked = await db.BandInvites
            .Where(i => i.Id == inviteId && i.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.RevokedAt, clock.GetCurrentInstant()), ct);
        return revoked == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static async Task<Results<Ok<AcceptInviteResponse>, NotFound, UnauthorizedHttpResult>> AcceptAsync(
        string token,
        ClaimsPrincipal principal,
        AppDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var now = clock.GetCurrentInstant();
        var hash = TokenHashing.Sha256Hex(token);
        var invite = await db.BandInvites
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (invite is null || invite.RevokedAt is not null || invite.ExpiresAt <= now)
        {
            return TypedResults.NotFound();
        }

        var bandName = await db.Bands
            .Where(b => b.Id == invite.BandId)
            .Select(b => b.Name)
            .SingleAsync(ct);

        var existing = await db.Memberships
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(m => m.BandId == invite.BandId && m.UserId == userId, ct);
        if (existing is not null)
        {
            return TypedResults.Ok(new AcceptInviteResponse(invite.BandId, bandName, existing.Id));
        }

        // Atomic use-claim: over-subscribed invites can't be double-spent by a race.
        var claimed = await db.BandInvites
            .IgnoreQueryFilters()
            .Where(i => i.Id == invite.Id && i.RevokedAt == null && (i.MaxUses == null || i.UseCount < i.MaxUses))
            .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.UseCount, i => i.UseCount + 1), ct);
        if (claimed == 0)
        {
            return TypedResults.NotFound();
        }

        var membership = new Membership
        {
            BandId = invite.BandId,
            UserId = userId,
            Role = BandRole.Member,
            JoinedAt = now,
        };
        db.Memberships.Add(membership);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Unique (BandId, UserId) lost a same-moment race — the membership exists.
            var winner = await db.Memberships
                .IgnoreQueryFilters()
                .SingleAsync(m => m.BandId == invite.BandId && m.UserId == userId, ct);
            return TypedResults.Ok(new AcceptInviteResponse(invite.BandId, bandName, winner.Id));
        }

        return TypedResults.Ok(new AcceptInviteResponse(invite.BandId, bandName, membership.Id));
    }
}
