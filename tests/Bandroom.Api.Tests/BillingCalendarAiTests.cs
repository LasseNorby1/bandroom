using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Ai;
using Bandroom.Api.Features.Availability;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Features.Billing;
using Bandroom.Api.Features.Events;
using Bandroom.Api.Features.Scheduling;
using Bandroom.Api.Tests.Support;
using Bandroom.Domain.Scheduling;
using NodaTime;
using Xunit;

namespace Bandroom.Api.Tests;

[Collection("api")]
public class BillingCalendarAiTests(ApiFactory factory)
{
    private async Task<(HttpClient Client, Guid BandId)> BandAsync()
    {
        var client = await factory.RegisterUserAsync("Lasse");
        var band = await (await client.PostAsJsonAsync("/bands", new CreateBandRequest("Overlay", null), TestJson.Options))
            .ReadAsAsync<BandDetailResponse>();
        return (client, band.Band.Id);
    }

    private static string Sign(string body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("test-webhook-secret"), Encoding.UTF8.GetBytes(body)));

    [Fact]
    public async Task Billing_WebhookFlipsPlan_OnlyWhenSigned()
    {
        var (client, bandId) = await BandAsync();
        var before = await (await client.GetAsync($"/bands/{bandId}/billing")).ReadAsAsync<BillingResponse>();
        Assert.Equal(BandPlan.Free, before.Plan);
        Assert.Contains($"checkout[custom][band_id]={bandId}", before.CheckoutUrl);

        var renews = SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromDays(30));
        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            meta = new { event_name = "subscription_created", custom_data = new { band_id = bandId.ToString() } },
            data = new
            {
                id = "sub_1",
                attributes = new
                {
                    status = "active",
                    customer_id = 42,
                    renews_at = NodaTime.Text.InstantPattern.ExtendedIso.Format(renews),
                    ends_at = (string?)null,
                    urls = new { customer_portal = "https://store.example.test/portal/1" },
                },
            },
        });
        using var anonymous = factory.CreateClient();

        var unsigned = new HttpRequestMessage(HttpMethod.Post, "/billing/webhooks/lemonsqueezy") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        unsigned.Headers.Add("X-Signature", "deadbeef");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(unsigned)).StatusCode);

        var signed = new HttpRequestMessage(HttpMethod.Post, "/billing/webhooks/lemonsqueezy") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        signed.Headers.Add("X-Signature", Sign(body));
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.SendAsync(signed)).StatusCode);

        var after = await (await client.GetAsync($"/bands/{bandId}/billing")).ReadAsAsync<BillingResponse>();
        Assert.Equal(BandPlan.Pro, after.Plan);
        Assert.True(after.IsPro);
        Assert.True(after.PlanExpiresAt > renews);
        Assert.Equal("https://store.example.test/portal/1", after.PortalUrl);

        var expired = body.Replace("subscription_created", "subscription_expired").Replace("\"active\"", "\"expired\"");
        var expireMsg = new HttpRequestMessage(HttpMethod.Post, "/billing/webhooks/lemonsqueezy") { Content = new StringContent(expired, Encoding.UTF8, "application/json") };
        expireMsg.Headers.Add("X-Signature", Sign(expired));
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.SendAsync(expireMsg)).StatusCode);
        // The test host runs with Entitlements:EveryBandPro (dev dogfood), so IsPro
        // can't flip here; the plan's own expiry is what the webhook controls.
        var afterExpiry = await (await client.GetAsync($"/bands/{bandId}/billing")).ReadAsAsync<BillingResponse>();
        Assert.True(afterExpiry.PlanExpiresAt <= SystemClock.Instance.GetCurrentInstant());
    }

    [Fact]
    public async Task CalendarOverlay_ImportsBusyDays_AndTheFinderSkipsThem()
    {
        var (client, bandId) = await BandAsync();
        // Open every evening so only the overlay can remove days.
        var all = Enum.GetValues<IsoDayOfWeek>().Where(d => d != IsoDayOfWeek.None).Select(d => new WeeklySlot(d, PracticeSlot.Evening)).ToList();
        await client.PutAsJsonAsync($"/bands/{bandId}/availability/me/pattern", new UpdatePatternRequest(all), TestJson.Options);

        var bad = await client.PutAsJsonAsync($"/bands/{bandId}/availability/me/calendar", new CalendarImportRequest("https://cal.example.test/missing.ics"), TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var notIcs = await client.PutAsJsonAsync($"/bands/{bandId}/availability/me/calendar", new CalendarImportRequest("https://cal.example.test/README.md"), TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, notIcs.StatusCode);
        Assert.Contains("ics calendar", await notIcs.Content.ReadAsStringAsync());
        Assert.Null((await (await client.GetAsync($"/bands/{bandId}/availability/me")).ReadAsAsync<MyAvailabilityResponse>()).CalendarImport.Url);

        var mine = await (await client.PutAsJsonAsync($"/bands/{bandId}/availability/me/calendar",
                new CalendarImportRequest("webcal://cal.example.test/private.ics"), TestJson.Options))
            .ReadAsAsync<MyAvailabilityResponse>();
        Assert.StartsWith("https://", mine.CalendarImport.Url);
        Assert.Null(mine.CalendarImport.Error);
        var today = FakeCalendarHandler.Today;
        Assert.Contains(mine.ImportedBusy, b => b.Date == today.PlusDays(3) && b.Slot == null && b.Summary == "Roskilde");
        Assert.Contains(mine.ImportedBusy, b => b.Date == today.PlusDays(5) && b.Slot == PracticeSlot.Evening && b.Summary == "Dinner");
        Assert.DoesNotContain(mine.ImportedBusy, b => b.Date == today.PlusDays(5) && b.Slot == PracticeSlot.Afternoon);

        var finder = await (await client.GetAsync($"/bands/{bandId}/practice-finder?days=10")).ReadAsAsync<PracticeFinderResponse>();
        Assert.DoesNotContain(finder.Candidates, c => c.Date == today.PlusDays(3));
        Assert.DoesNotContain(finder.Candidates, c => c.Date == today.PlusDays(5));
        Assert.Contains(finder.Candidates, c => c.Date == today.PlusDays(4));

        var overview = await (await client.GetAsync($"/bands/{bandId}/availability")).ReadAsAsync<BandAvailabilityResponse>();
        Assert.Equal(2, overview.Members.Single().ImportedBusy.Count);

        var cleared = await (await client.PutAsJsonAsync($"/bands/{bandId}/availability/me/calendar", new CalendarImportRequest(""), TestJson.Options))
            .ReadAsAsync<MyAvailabilityResponse>();
        Assert.Null(cleared.CalendarImport.Url);
        Assert.Empty(cleared.ImportedBusy);
    }

    [Fact]
    public async Task Ai_ParsesBlockoutsAndOffers_RespectsTheBandSwitch()
    {
        var (client, bandId) = await BandAsync();
        var today = FakeCalendarHandler.Today;
        factory.Ai.IsConfigured = true;
        factory.Ai.NextJson = $$"""
            {"blockouts":[{"from":"{{today.PlusDays(2):yyyy-MM-dd}}","to":"{{today.PlusDays(3):yyyy-MM-dd}}","note":"weekend away"},
                          {"from":"2001-01-01","to":"2001-01-02","note":"ancient"}],
             "unparsed":null}
            """;
        var parsed = await (await client.PostAsJsonAsync($"/bands/{bandId}/availability/me/exceptions/parse",
                new ParseTextRequest("can't this weekend, away"), TestJson.Options)).ReadAsAsync<ParsedBlockoutsResponse>();
        var only = Assert.Single(parsed.Blockouts); // the past one is dropped
        Assert.Equal(today.PlusDays(2), only.From);
        Assert.Contains("Europe/Copenhagen", factory.Ai.LastSystem);
        Assert.Equal("can't this weekend, away", factory.Ai.LastUser);

        factory.Ai.NextJson = """{"type":"gig","date":"2027-05-01","startTime":"21:00","endTime":null,"title":"Gig at Loppen","location":"Loppen, Christiania","fee":"3.000 kr","notes":"load-in 17:00"}""";
        var offer = await (await client.PostAsJsonAsync($"/bands/{bandId}/events/parse-offer",
                new ParseTextRequest("Hi! Would you play Loppen May 1st? 3.000 kr, load-in 17, on at 21."), TestJson.Options)).ReadAsAsync<ParsedGigOffer>();
        Assert.Equal(EventType.Gig, offer.Type);
        Assert.Equal(new LocalDate(2027, 5, 1), offer.Date);
        Assert.Equal(new LocalTime(21, 0), offer.StartTime);
        Assert.Equal("3.000 kr", offer.Fee);

        // Switch ai off for the band → 409; unconfigured server → 503.
        await client.PatchAsJsonAsync($"/bands/{bandId}", new UpdateBandRequest(null, null, null, null, null, AiEnabled: false), TestJson.Options);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/bands/{bandId}/availability/me/exceptions/parse",
            new ParseTextRequest("x"), TestJson.Options)).StatusCode);
        await client.PatchAsJsonAsync($"/bands/{bandId}", new UpdateBandRequest(null, null, null, null, null, AiEnabled: true), TestJson.Options);
        factory.Ai.IsConfigured = false;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync($"/bands/{bandId}/availability/me/exceptions/parse",
            new ParseTextRequest("x"), TestJson.Options)).StatusCode);
        factory.Ai.IsConfigured = true;
    }

    [Fact]
    public async Task CatchUp_ListsWhatOthersDid_AndSummarisesWhenAiIsOn()
    {
        var admin = await factory.RegisterUserAsync("Admin");
        var band = await (await admin.PostAsJsonAsync("/bands", new CreateBandRequest("Catch", null), TestJson.Options)).ReadAsAsync<BandDetailResponse>();
        var invite = await (await admin.PostAsJsonAsync($"/bands/{band.Band.Id}/invites", new CreateInviteRequest(null, null), TestJson.Options)).ReadAsAsync<InviteCreatedResponse>();
        var member = await factory.RegisterUserAsync("Member");
        await member.PostAsync($"/invites/{invite.Token}/accept", null);
        await member.PostAsync($"/bands/{band.Band.Id}/catch-up/seen", null);

        // The admin does things while the member is away.
        var evt = await (await admin.PostAsJsonAsync($"/bands/{band.Band.Id}/events",
            new CreateEventRequest(EventType.Gig, FakeCalendarHandler.Today.PlusDays(12), Title: "Loppen"), TestJson.Options)).ReadAsAsync<EventDetailResponse>();
        await admin.PostAsJsonAsync($"/bands/{band.Band.Id}/comments",
            new Bandroom.Api.Features.Comments.CreateCommentRequest(CommentTarget.Event, evt.Event.Id, "Bring the small amp"), TestJson.Options);
        await admin.PutAsJsonAsync($"/bands/{band.Band.Id}/notice", new UpdateNoticeRequest("Door code 1234"), TestJson.Options);

        factory.Ai.IsConfigured = true;
        factory.Ai.NextText = "Admin booked a gig at Loppen and pinned the door code.";
        var catchUp = await (await member.GetAsync($"/bands/{band.Band.Id}/catch-up")).ReadAsAsync<CatchUpResponse>();
        Assert.NotNull(catchUp.Since);
        Assert.Contains(catchUp.Items, i => i.Kind == "event" && i.Text.Contains("Loppen"));
        Assert.Contains(catchUp.Items, i => i.Kind == "comment" && i.Text.Contains("small amp"));
        Assert.Contains(catchUp.Items, i => i.Kind == "notice");
        Assert.Equal("Admin booked a gig at Loppen and pinned the door code.", catchUp.Summary);
        Assert.Contains("[event]", factory.Ai.LastUser);

        // Your own actions never show up for you.
        var own = await (await admin.GetAsync($"/bands/{band.Band.Id}/catch-up")).ReadAsAsync<CatchUpResponse>();
        Assert.DoesNotContain(own.Items, i => i.Kind == "event");
    }
}
