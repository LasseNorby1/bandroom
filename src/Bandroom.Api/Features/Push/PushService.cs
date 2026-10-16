using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bandroom.Api.Data;
using Hangfire;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Push;

public sealed record PushOptions
{
    /// <summary>VAPID keys (base64url, P-256). `dotnet Bandroom.Api.dll --vapid` prints a fresh pair.</summary>
    public string? PublicKey { get; init; }

    public string? PrivateKey { get; init; }

    public string Subject { get; init; } = "mailto:hello@bandroom.io";

    public bool IsConfigured => !string.IsNullOrEmpty(PublicKey) && !string.IsNullOrEmpty(PrivateKey);
}

/// <summary>A notification: what the service worker shows and where a tap goes. Actions are one-tap answers.</summary>
public sealed record PushAction(string Action, string Title, string Url);

public sealed record PushPayload(string Title, string Body, string Url, string Tag, IReadOnlyList<PushAction>? Actions = null);

public interface IPushSender
{
    /// <summary>Queue a notification for every device of one user. Fire-and-forget; never in the request's critical path.</summary>
    void Send(Guid userId, PushPayload payload);
}

/// <summary>Production: one Hangfire job per user, so a dead endpoint never slows a request.</summary>
public sealed class HangfirePushSender(PushOptions options) : IPushSender
{
    public void Send(Guid userId, PushPayload payload)
    {
        if (!options.IsConfigured)
        {
            return;
        }

        var json = JsonSerializer.Serialize(payload, PushJson.Options);
        BackgroundJob.Enqueue<PushDeliveryJob>(job => job.DeliverAsync(userId, json, CancellationToken.None));
    }
}

/// <summary>Test hosts and jobs-disabled hosts: nothing leaves the process.</summary>
public sealed class NoopPushSender(ILogger<NoopPushSender> logger) : IPushSender
{
    public void Send(Guid userId, PushPayload payload) =>
        logger.LogDebug("push (noop) to {UserId}: {Title}", userId, payload.Title);
}

public static class PushJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public sealed class PushDeliveryJob(
    AppDbContext db, PushOptions options, IHttpClientFactory httpClients, IClock clock, ILogger<PushDeliveryJob> logger)
{

    public async Task DeliverAsync(Guid userId, string payloadJson, CancellationToken ct)
    {
        if (!options.IsConfigured)
        {
            return;
        }

        var subscriptions = await db.PushSubscriptions.Where(s => s.UserId == userId).ToListAsync(ct);
        if (subscriptions.Count == 0)
        {
            return;
        }

        var tag = JsonSerializer.Deserialize<PushPayload>(payloadJson, PushJson.Options)?.Tag;
        var auth = new VapidAuthentication(options.PublicKey!, options.PrivateKey!) { Subject = options.Subject };
        var client = new PushServiceClient(httpClients.CreateClient("push"));
        foreach (var sub in subscriptions)
        {
            var target = new Lib.Net.Http.WebPush.PushSubscription { Endpoint = sub.Endpoint };
            target.SetKey(PushEncryptionKeyName.P256DH, sub.P256dh);
            target.SetKey(PushEncryptionKeyName.Auth, sub.Auth);
            var message = new PushMessage(payloadJson) { Topic = tag, Urgency = PushMessageUrgency.High, TimeToLive = 60 * 60 * 24 };
            try
            {
                await client.RequestPushMessageDeliveryAsync(target, message, auth, ct);
                sub.LastUsedAt = clock.GetCurrentInstant();
            }
            catch (PushServiceClientException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
            {
                // The browser dropped the subscription; forget it.
                db.Remove(sub);
            }
            catch (PushServiceClientException ex)
            {
                logger.LogWarning("push to {Endpoint} failed: {Status}", sub.Endpoint[..Math.Min(40, sub.Endpoint.Length)], ex.StatusCode);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// One-tap answers from a notification: a signed, short-lived token that names
/// a membership and an event. The service worker posts it; no session needed.
/// </summary>
public static class RsvpTokens
{
    public static string Create(string signingKey, Guid membershipId, Guid eventId, Instant expires)
    {
        var body = $"{membershipId:N}.{eventId:N}.{expires.ToUnixTimeSeconds()}";
        return $"{body}.{Sign(signingKey, body)}";
    }

    public static (Guid MembershipId, Guid EventId)? Parse(string signingKey, string token, Instant now)
    {
        var parts = token.Split('.');
        if (parts.Length != 4)
        {
            return null;
        }

        var body = $"{parts[0]}.{parts[1]}.{parts[2]}";
        var expected = Sign(signingKey, body);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(parts[3]), Encoding.UTF8.GetBytes(expected)))
        {
            return null;
        }

        if (!long.TryParse(parts[2], out var exp) || Instant.FromUnixTimeSeconds(exp) < now)
        {
            return null;
        }

        return Guid.TryParseExact(parts[0], "N", out var m) && Guid.TryParseExact(parts[1], "N", out var e) ? (m, e) : null;
    }

    private static string Sign(string key, string body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("rsvp:" + key), Encoding.UTF8.GetBytes(body)))[..32];
}

public static class Vapid
{
    /// <summary>A fresh P-256 pair in the base64url form web push wants.</summary>
    public static (string PublicKey, string PrivateKey) GenerateKeys()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(true);
        var publicKey = new byte[65];
        publicKey[0] = 0x04;
        p.Q.X!.CopyTo(publicKey, 1);
        p.Q.Y!.CopyTo(publicKey, 33);
        return (Base64Url(publicKey), Base64Url(p.D!));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
