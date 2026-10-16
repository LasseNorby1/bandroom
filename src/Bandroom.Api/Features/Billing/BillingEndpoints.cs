using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;

namespace Bandroom.Api.Features.Billing;

/// <summary>
/// Merchant-of-record billing (spec §8 p4): Lemon Squeezy carries eu vat/oss.
/// Nothing here is live until the store exists — with no CheckoutUrl the ui
/// shows no upgrade button, with no WebhookSecret the webhook answers 503.
/// </summary>
public sealed record BillingOptions
{
    /// <summary>The product's hosted checkout url. The band id rides along as custom data.</summary>
    public string? CheckoutUrl { get; init; }

    /// <summary>Signing secret from the webhook settings; hmac-sha256 over the raw body.</summary>
    public string? WebhookSecret { get; init; }

    /// <summary>Days of grace after a renewal date before a band drops to free.</summary>
    public int GraceDays { get; init; } = 3;
}

public sealed record BillingResponse(
    BandPlan Plan,
    bool IsPro,
    Instant? PlanExpiresAt,
    /// <summary>Where an admin goes to pay; null when billing isn't configured or the caller isn't admin.</summary>
    string? CheckoutUrl,
    /// <summary>The provider's customer portal for the band's subscription, once one exists.</summary>
    string? PortalUrl);

public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGroup("/bands/{bandId:guid}")
            .RequireAuthorization()
            .WithTags("billing")
            .AddEndpointFilter<BandMemberFilter>()
            .MapGet("/billing", GetAsync)
            .WithSummary("Plan, expiry and where to upgrade");

        routes.MapPost("/billing/webhooks/lemonsqueezy", LemonSqueezyWebhookAsync)
            .AllowAnonymous()
            .WithTags("billing")
            .WithSummary("Lemon Squeezy subscription webhook (signed)");

        return routes;
    }

    private static Ok<BillingResponse> GetAsync(
        BandContext bandContext,
        BillingOptions billing,
        EntitlementOptions entitlements,
        IClock clock)
    {
        var band = bandContext.Band;
        var now = clock.GetCurrentInstant();
        string? checkout = null;
        if (bandContext.Role == BandRole.Admin && !string.IsNullOrEmpty(billing.CheckoutUrl))
        {
            var separator = billing.CheckoutUrl.Contains('?') ? "&" : "?";
            checkout = $"{billing.CheckoutUrl}{separator}checkout[custom][band_id]={band.Id}";
        }

        return TypedResults.Ok(new BillingResponse(
            band.Plan,
            PlanLimits.IsPro(band, now, entitlements),
            band.PlanExpiresAt,
            checkout,
            band.BillingPortalUrl));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, BadRequest<string>, StatusCodeHttpResult>> LemonSqueezyWebhookAsync(
        HttpRequest request,
        AppDbContext db,
        BillingOptions billing,
        IClock clock,
        ILogger<BillingOptions> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(billing.WebhookSecret))
        {
            return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(ct);
        var signature = request.Headers["X-Signature"].ToString();
        var expected = Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(billing.WebhookSecret), Encoding.UTF8.GetBytes(body)));
        if (signature.Length == 0 ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(signature.ToLowerInvariant()), Encoding.UTF8.GetBytes(expected)))
        {
            return TypedResults.Unauthorized();
        }

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var eventName = root.GetProperty("meta").GetProperty("event_name").GetString() ?? "";
        if (!root.GetProperty("meta").TryGetProperty("custom_data", out var custom) ||
            !custom.TryGetProperty("band_id", out var bandIdElement) ||
            !Guid.TryParse(bandIdElement.GetString(), out var bandId))
        {
            return TypedResults.BadRequest("custom_data.band_id missing");
        }

        var band = await db.Bands.SingleOrDefaultAsync(b => b.Id == bandId, ct);
        if (band is null)
        {
            // Not ours (another environment's store?) — acknowledge so they stop retrying.
            logger.LogWarning("billing webhook {Event} for unknown band {BandId}", eventName, bandId);
            return TypedResults.NoContent();
        }

        var data = root.GetProperty("data");
        var attributes = data.GetProperty("attributes");
        var status = attributes.TryGetProperty("status", out var s) ? s.GetString() : null;
        var renewsAt = ReadInstant(attributes, "renews_at");
        var endsAt = ReadInstant(attributes, "ends_at");
        var now = clock.GetCurrentInstant();
        var grace = Duration.FromDays(billing.GraceDays);

        switch (eventName)
        {
            case "subscription_created":
            case "subscription_updated":
            case "subscription_resumed":
            case "subscription_unpaused":
            case "subscription_payment_success":
            case "subscription_payment_recovered":
                if (status is "active" or "on_trial" or "past_due" or "cancelled")
                {
                    band.Plan = BandPlan.Pro;
                    // Cancelled keeps running until ends_at; active until the next renewal (+grace).
                    band.PlanExpiresAt = status == "cancelled" ? endsAt ?? renewsAt : (renewsAt ?? now).Plus(grace);
                }

                break;
            case "subscription_cancelled":
                band.Plan = BandPlan.Pro;
                band.PlanExpiresAt = endsAt ?? renewsAt ?? now;
                break;
            case "subscription_expired":
            case "subscription_paused":
                band.PlanExpiresAt = endsAt is { } e && e < now ? e : now;
                break;
            default:
                logger.LogInformation("billing webhook {Event} ignored", eventName);
                return TypedResults.NoContent();
        }

        band.BillingSubscriptionId = data.TryGetProperty("id", out var id) ? id.GetString() : band.BillingSubscriptionId;
        if (attributes.TryGetProperty("customer_id", out var customer))
        {
            band.BillingCustomerId = customer.ValueKind == JsonValueKind.Number ? customer.GetInt64().ToString() : customer.GetString();
        }

        if (attributes.TryGetProperty("urls", out var urls) && urls.TryGetProperty("customer_portal", out var portal))
        {
            band.BillingPortalUrl = portal.GetString();
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("billing: band {BandId} {Event} → {Plan} until {Until}", band.Id, eventName, band.Plan, band.PlanExpiresAt);
        return TypedResults.NoContent();
    }

    private static Instant? ReadInstant(JsonElement attributes, string name)
    {
        if (!attributes.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var parsed = InstantPattern.ExtendedIso.Parse(value.GetString()!);
        return parsed.Success ? parsed.Value : null;
    }
}
