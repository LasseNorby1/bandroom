using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Auth;
using Bandroom.Api.Features.Events;
using Bandroom.Api.Infrastructure.Email;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using NodaTime;

namespace Bandroom.Api.Features.Push;

public sealed record PushKeysResponse(bool Enabled, string? PublicKey);

public sealed record SubscribeRequest(string Endpoint, SubscribeKeys Keys, string? UserAgent = null);

public sealed record SubscribeKeys(string P256dh, string Auth);

public sealed record UnsubscribeRequest(string Endpoint);

public static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder routes)
    {
        var push = routes.MapGroup("/push").WithTags("push");

        push.MapGet("/keys", Keys).WithSummary("Whether push is on, and the VAPID public key");
        push.MapPost("/subscriptions", SubscribeAsync).RequireAuthorization().WithSummary("Register this browser for notifications");
        push.MapDelete("/subscriptions", UnsubscribeAsync).RequireAuthorization().WithSummary("Forget this browser");
        push.MapPost("/rsvp", RsvpFromNotificationAsync).AllowAnonymous()
            .WithSummary("One-tap rsvp from a notification action (signed token)");

        return routes;
    }

    private static Ok<PushKeysResponse> Keys(PushOptions options) =>
        TypedResults.Ok(new PushKeysResponse(options.IsConfigured, options.PublicKey));

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ValidationProblem>> SubscribeAsync(
        SubscribeRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" || request.Endpoint.Length > 1000 ||
            string.IsNullOrEmpty(request.Keys?.P256dh) || string.IsNullOrEmpty(request.Keys.Auth))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["endpoint"] = ["Not a push subscription."] });
        }

        var existing = await db.PushSubscriptions.SingleOrDefaultAsync(s => s.Endpoint == request.Endpoint, ct);
        if (existing is not null)
        {
            if (existing.UserId != userId)
            {
                // A shared device changed hands; the endpoint follows the signed-in person.
                db.Remove(existing);
                existing = null;
            }
            else
            {
                existing.LastUsedAt = clock.GetCurrentInstant();
            }
        }

        if (existing is null)
        {
            db.Add(new PushSubscription
            {
                UserId = userId,
                Endpoint = request.Endpoint,
                P256dh = request.Keys.P256dh,
                Auth = request.Keys.Auth,
                UserAgent = request.UserAgent is { Length: > 200 } ua ? ua[..200] : request.UserAgent,
                CreatedAt = clock.GetCurrentInstant(),
            });
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult>> UnsubscribeAsync(
        [FromBody] UnsubscribeRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        await db.PushSubscriptions.Where(s => s.Endpoint == request.Endpoint && s.UserId == userId).ExecuteDeleteAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<EventSummaryResponse>, NotFound, BadRequest<string>>> RsvpFromNotificationAsync(
        string token,
        string status,
        AppDbContext db,
        AuthOptions authOptions,
        IClock clock,
        BandNotifier notifier,
        IAppEmailSender email,
        IPushSender push,
        CancellationToken ct)
    {
        var now = clock.GetCurrentInstant();
        if (RsvpTokens.Parse(authOptions.JwtSigningKey, token, now) is not { } parsed)
        {
            return TypedResults.BadRequest("That link has expired — open the event instead.");
        }

        if (!Enum.TryParse<RsvpStatus>(status, ignoreCase: true, out var rsvpStatus))
        {
            return TypedResults.BadRequest("Unknown answer.");
        }

        // No band context on this anonymous route: the token names the membership,
        // and every lookup is explicit.
        var membership = await db.Memberships.IgnoreQueryFilters()
            .SingleOrDefaultAsync(m => m.Id == parsed.MembershipId && m.LeftAt == null, ct);
        var evt = await db.Events.IgnoreQueryFilters().SingleOrDefaultAsync(e => e.Id == parsed.EventId, ct);
        if (membership is null || evt is null || evt.BandId != membership.BandId || evt.Status == EventStatus.Cancelled)
        {
            return TypedResults.NotFound();
        }

        var band = await db.Bands.SingleAsync(b => b.Id == evt.BandId, ct);
        var summary = await RsvpService.ApplyAsync(db, band, membership, evt, rsvpStatus, null, now, notifier, email, push, authOptions, ct);
        return TypedResults.Ok(summary);
    }
}
