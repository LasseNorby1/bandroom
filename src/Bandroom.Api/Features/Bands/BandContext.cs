using System.Security.Claims;
using Bandroom.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Bandroom.Api.Features.Bands;

/// <summary>
/// Request-scoped "who am I in which band". Populated exactly once, by
/// <see cref="BandMemberFilter"/>; the query filters in AppDbContext read it.
/// </summary>
public sealed class BandContext : IBandContext
{
    private Band? _band;

    public Guid? BandId { get; private set; }

    public Guid? MembershipId { get; private set; }

    public BandRole? Role { get; private set; }

    /// <summary>
    /// The band itself, loaded (tracked) alongside the membership so handlers
    /// never re-query it. Mutations on it are picked up by the request's
    /// SaveChanges like any other tracked entity.
    /// </summary>
    public Band Band => _band ?? throw new InvalidOperationException("No band context — is the endpoint behind BandMemberFilter?");

    public void Set(Membership membership, Band band)
    {
        BandId = membership.BandId;
        MembershipId = membership.Id;
        Role = membership.Role;
        _band = band;
    }
}

/// <summary>
/// Resolves the caller's membership for /bands/{bandId}/** routes. Non-members
/// get 404, not 403 — for outsiders a band does not exist, so band ids can't be
/// probed.
/// </summary>
public sealed class BandMemberFilter(AppDbContext db, BandContext bandContext) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.HttpContext.Request.RouteValues["bandId"] is not string raw ||
            !Guid.TryParse(raw, out var bandId))
        {
            return TypedResults.NotFound();
        }

        var subject = context.HttpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        // No band context exists yet — this lookup is the one that creates it.
        // Membership and band come back in one round trip; both stay tracked so
        // handlers can mutate the band (storage accounting, settings) directly.
        var hit = await db.Memberships
            .IgnoreQueryFilters()
            .Where(m => m.BandId == bandId && m.UserId == userId)
            .Join(db.Bands, m => m.BandId, b => b.Id, (m, b) => new { Membership = m, Band = b })
            .SingleOrDefaultAsync();
        if (hit is null)
        {
            return TypedResults.NotFound();
        }

        bandContext.Set(hit.Membership, hit.Band);
        return await next(context);
    }
}

/// <summary>Runs after BandMemberFilter; membership exists, only the role is checked.</summary>
public sealed class RequireBandAdminFilter(BandContext bandContext) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        bandContext.Role == BandRole.Admin ? await next(context) : TypedResults.Forbid();
}
