using System.Net;
using System.Net.Http.Json;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Auth;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Features.Events;
using Bandroom.Api.Features.Push;
using Bandroom.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;
using Xunit;

namespace Bandroom.Api.Tests;

[Collection("api")]
public class PushTests(ApiFactory factory)
{
    private static LocalDate Soon(int days) =>
        SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb["Europe/Copenhagen"]).Date.PlusDays(days);

    private async Task<(HttpClient Admin, HttpClient Member, Guid BandId, Guid AdminUserId, Guid MemberUserId, Guid MemberMembershipId)> BandWithTwoAsync()
    {
        var admin = await factory.RegisterUserAsync("Admin");
        var adminMe = await (await admin.GetAsync("/auth/me")).ReadAsAsync<MeResponse>();
        var band = await (await admin.PostAsJsonAsync("/bands", new CreateBandRequest("Push", null), TestJson.Options)).ReadAsAsync<BandDetailResponse>();
        var invite = await (await admin.PostAsJsonAsync($"/bands/{band.Band.Id}/invites", new CreateInviteRequest(null, null), TestJson.Options)).ReadAsAsync<InviteCreatedResponse>();
        var member = await factory.RegisterUserAsync("Member");
        var memberMe = await (await member.GetAsync("/auth/me")).ReadAsAsync<MeResponse>();
        var accepted = await (await member.PostAsync($"/invites/{invite.Token}/accept", null)).ReadAsAsync<AcceptInviteResponse>();
        return (admin, member, band.Band.Id, adminMe.Id, memberMe.Id, accepted.MembershipId);
    }

    [Fact]
    public async Task Subscriptions_RegisterAndForget_RequireHttpsEndpoints()
    {
        var client = await factory.RegisterUserAsync("Subscriber");
        var keys = await (await client.GetAsync("/push/keys")).ReadAsAsync<PushKeysResponse>();
        Assert.False(keys.Enabled); // test host has no vapid keys; the ui hides the switch

        var bad = await client.PostAsJsonAsync("/push/subscriptions",
            new SubscribeRequest("http://insecure.example/x", new SubscribeKeys("p", "a")), TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var endpoint = $"https://push.example.test/{Guid.NewGuid():N}";
        var ok = await client.PostAsJsonAsync("/push/subscriptions",
            new SubscribeRequest(endpoint, new SubscribeKeys("p256", "auth"), "test-agent"), TestJson.Options);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        // Idempotent re-register.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/push/subscriptions",
            new SubscribeRequest(endpoint, new SubscribeKeys("p256", "auth")), TestJson.Options)).StatusCode);

        var gone = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/push/subscriptions")
        {
            Content = JsonContent.Create(new UnsubscribeRequest(endpoint), options: TestJson.Options),
        });
        Assert.Equal(HttpStatusCode.NoContent, gone.StatusCode);
    }

    [Fact]
    public async Task Proposal_PushesEveryoneElse_WithOneTapAnswersThatWork()
    {
        var (admin, member, bandId, adminUserId, memberUserId, memberMembershipId) = await BandWithTwoAsync();
        var before = factory.Push.Sent.Count;

        var evt = await (await admin.PostAsJsonAsync($"/bands/{bandId}/events",
            new CreateEventRequest(EventType.Practice, Soon(6)), TestJson.Options)).ReadAsAsync<EventDetailResponse>();

        var sent = factory.Push.Sent.Skip(before).ToList();
        var toMember = Assert.Single(sent, p => p.UserId == memberUserId);
        Assert.DoesNotContain(sent, p => p.UserId == adminUserId); // the proposer isn't asked
        Assert.Contains("proposed", toMember.Payload.Title);
        Assert.NotNull(toMember.Payload.Actions);
        var yes = toMember.Payload.Actions!.Single(a => a.Action == "going");
        Assert.StartsWith("/push/rsvp?token=", yes.Url);

        // The notification's button posts the signed url with no session at all.
        using var anonymous = factory.CreateClient();
        var answered = await (await anonymous.PostAsync(yes.Url, null)).ReadAsAsync<EventSummaryResponse>();
        Assert.Equal(RsvpStatus.Going, answered.MyRsvp);
        Assert.Equal(EventStatus.Confirmed, answered.Status); // 2 of 2 → quorum → confirmed
        Assert.Equal(2, answered.GoingCount);

        // Confirmation pushes everyone, including the proposer.
        var confirmPushes = factory.Push.Sent.Skip(before).Where(p => p.Payload.Title.EndsWith("is on")).ToList();
        Assert.Contains(confirmPushes, p => p.UserId == adminUserId);
        Assert.Contains(confirmPushes, p => p.UserId == memberUserId);

        // A tampered token is refused; the member's real session still sees their answer.
        var tampered = await anonymous.PostAsync(yes.Url[..^4] + "zzzz", null);
        Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);
        var detail = await (await member.GetAsync($"/bands/{bandId}/events/{evt.Event.Id}")).ReadAsAsync<EventDetailResponse>();
        Assert.Equal(RsvpStatus.Going, detail.Rsvps.Single(r => r.MembershipId == memberMembershipId).Status);
    }

    [Fact]
    public async Task Delivery_EncryptsForEachDevice_AndForgetsDroppedOnes()
    {
        var client = await factory.RegisterUserAsync("Phones");
        var me = await (await client.GetAsync("/auth/me")).ReadAsAsync<MeResponse>();
        var live = $"https://push.example.test/live/{Guid.NewGuid():N}";
        var gone = $"https://push.example.test/gone/{Guid.NewGuid():N}";
        foreach (var endpoint in new[] { live, gone })
        {
            // Real browser-shaped keys: an uncompressed P-256 point and a 16-byte auth secret.
            using var ecdh = System.Security.Cryptography.ECDiffieHellman.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
            var q = ecdh.ExportParameters(false).Q;
            var p256dh = Base64Url(new byte[] { 4 }.Concat(q.X!).Concat(q.Y!).ToArray());
            var auth = Base64Url(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
            var res = await client.PostAsJsonAsync("/push/subscriptions", new SubscribeRequest(endpoint, new SubscribeKeys(p256dh, auth)), TestJson.Options);
            Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        }

        var (publicKey, privateKey) = Vapid.GenerateKeys();
        using var scope = factory.Services.CreateScope();
        var job = new PushDeliveryJob(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            new PushOptions { PublicKey = publicKey, PrivateKey = privateKey },
            scope.ServiceProvider.GetRequiredService<IHttpClientFactory>(),
            scope.ServiceProvider.GetRequiredService<IClock>(),
            scope.ServiceProvider.GetRequiredService<ILogger<PushDeliveryJob>>());
        var payload = System.Text.Json.JsonSerializer.Serialize(new PushPayload("Hi", "there", "/home", "t-1"), PushJson.Options);
        await job.DeliverAsync(me.Id, payload, CancellationToken.None);

        var deliveries = factory.PushService.Deliveries.Where(d => d.Endpoint.ToString() is var e && (e == live || e == gone)).ToList();
        Assert.Equal(2, deliveries.Count);
        Assert.All(deliveries, d =>
        {
            Assert.StartsWith("vapid", d.Authorization);
            Assert.Equal("aes128gcm", d.Encoding);
            Assert.Equal("t-1", d.Topic);
            Assert.True(d.BodyLength > payload.Length); // encrypted, with salt and key header
        });

        // The 410 endpoint is gone from the table; the live one stays.
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var left = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(db.PushSubscriptions.Where(s => s.UserId == me.Id));
        Assert.Equal([live], left.Select(s => s.Endpoint));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void RsvpTokens_ExpireAndBindToKey()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var m = Guid.NewGuid();
        var e = Guid.NewGuid();
        var token = RsvpTokens.Create("key-one", m, e, now.Plus(Duration.FromHours(1)));
        Assert.Equal((m, e), RsvpTokens.Parse("key-one", token, now));
        Assert.Null(RsvpTokens.Parse("key-two", token, now));
        Assert.Null(RsvpTokens.Parse("key-one", token, now.Plus(Duration.FromHours(2))));
        Assert.Null(RsvpTokens.Parse("key-one", "not.a.token", now));
    }

    [Fact]
    public void Vapid_KeysHaveWebPushShape()
    {
        var (publicKey, privateKey) = Vapid.GenerateKeys();
        Assert.Equal(87, publicKey.Length); // 65 bytes, base64url, no padding
        Assert.Equal(43, privateKey.Length); // 32 bytes
        Assert.DoesNotContain('=', publicKey);
        Assert.DoesNotContain('+', publicKey);
    }
}
