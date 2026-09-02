using System.Net;
using System.Net.Http.Json;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Availability;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Features.Events;
using Bandroom.Api.Features.Jobs;
using Bandroom.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Xunit;

namespace Bandroom.Api.Tests;

[Collection("api")]
public class CalendarAndJobsTests(ApiFactory factory)
{
    private static readonly DateTimeZone Copenhagen = DateTimeZoneProviders.Tzdb["Europe/Copenhagen"];

    private static LocalDate CopenhagenToday =>
        SystemClock.Instance.GetCurrentInstant().InZone(Copenhagen).Date;

    private async Task<(HttpClient Admin, HttpClient Member, Guid BandId, string BandName)> BandWithTwoMembersAsync()
    {
        var bandName = $"Band-{Guid.NewGuid():N}";
        var admin = await factory.RegisterUserAsync("Lasse");
        var created = await (await admin.PostAsJsonAsync(
                "/bands", new CreateBandRequest(bandName, null), TestJson.Options))
            .ReadAsAsync<BandDetailResponse>();
        var invite = await (await admin.PostAsJsonAsync(
                $"/bands/{created.Band.Id}/invites", new CreateInviteRequest(null, null), TestJson.Options))
            .ReadAsAsync<InviteCreatedResponse>();
        var member = await factory.RegisterUserAsync("Sofie");
        (await member.PostAsync($"/invites/{invite.Token}/accept", null)).EnsureSuccessStatusCode();
        return (admin, member, created.Band.Id, bandName);
    }

    [Fact]
    public async Task IcsFeed_ServesTheBandCalendar()
    {
        var (admin, _, bandId, bandName) = await BandWithTwoMembersAsync();
        var date = CopenhagenToday.PlusDays(21);

        await admin.PostAsJsonAsync(
            $"/bands/{bandId}/events",
            new CreateEventRequest(EventType.Practice, date, Confirmed: true), TestJson.Options);
        await admin.PostAsJsonAsync(
            $"/bands/{bandId}/events",
            new CreateEventRequest(EventType.Gig, date.PlusDays(7), null, new LocalTime(20, 0),
                new LocalTime(23, 0), $"Release-{bandName}", "Vega"), TestJson.Options);

        var mine = await (await admin.GetAsync($"/bands/{bandId}/availability/me"))
            .ReadAsAsync<MyAvailabilityResponse>();

        var feed = await factory.CreateClient().GetAsync(mine.CalendarFeedPath);
        feed.EnsureSuccessStatusCode();
        Assert.Equal("text/calendar", feed.Content.Headers.ContentType!.MediaType);
        var ics = await feed.Content.ReadAsStringAsync();

        Assert.Contains("BEGIN:VCALENDAR", ics);
        Assert.Contains($"Practice — {bandName}", ics);
        Assert.Contains("STATUS:CONFIRMED", ics);
        // The proposed gig rides along as tentative.
        Assert.Contains($"Release-{bandName}", ics);
        Assert.Contains("STATUS:TENTATIVE", ics);
        Assert.Contains("LOCATION:Vega", ics);
    }

    [Fact]
    public async Task IcsFeed_UnknownToken_Is404()
    {
        var response = await factory.CreateClient().GetAsync("/calendar/not-a-token.ics");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReminderJob_SendsOnce_TheEveningBefore()
    {
        var (admin, _, bandId, bandName) = await BandWithTwoMembersAsync();
        var tomorrow = CopenhagenToday.PlusDays(1);
        await admin.PostAsJsonAsync(
            $"/bands/{bandId}/events",
            new CreateEventRequest(EventType.Practice, tomorrow, Confirmed: true), TestJson.Options);

        var eveningBefore = Copenhagen.AtLeniently(CopenhagenToday.At(new LocalTime(18, 0))).ToInstant();
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReminderJob>().RunAtAsync(eveningBefore, default);
            // Second run must be a no-op — the reminder is sent exactly once.
            await scope.ServiceProvider.GetRequiredService<ReminderJob>().RunAtAsync(eveningBefore, default);
        }

        var recorder = factory.Services.GetRequiredService<RecordingEmailSender>();
        var reminders = recorder.Sent
            .Where(mail => mail.Subject.Contains($"Practice — {bandName}"))
            .ToList();
        Assert.Equal(2, reminders.Count); // both members, once each
        Assert.All(reminders, mail => Assert.StartsWith("Tomorrow:", mail.Subject));

        using var verifyScope = factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var evt = await db.Events.IgnoreQueryFilters().SingleAsync(e => e.BandId == bandId);
        Assert.NotNull(evt.ReminderSentAt);
    }

    [Fact]
    public async Task ReminderJob_TooEarlyInTheDay_SendsNothing()
    {
        var (admin, _, bandId, bandName) = await BandWithTwoMembersAsync();
        var tomorrow = CopenhagenToday.PlusDays(1);
        await admin.PostAsJsonAsync(
            $"/bands/{bandId}/events",
            new CreateEventRequest(EventType.Practice, tomorrow, Confirmed: true), TestJson.Options);

        var morning = Copenhagen.AtLeniently(CopenhagenToday.At(new LocalTime(9, 0))).ToInstant();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ReminderJob>().RunAtAsync(morning, default);

        var recorder = factory.Services.GetRequiredService<RecordingEmailSender>();
        Assert.DoesNotContain(recorder.Sent, mail => mail.Subject.Contains($"Practice — {bandName}"));
    }

    [Fact]
    public async Task DigestJob_MailsEveryMemberTheAgendaAndNudge()
    {
        var (admin, _, bandId, bandName) = await BandWithTwoMembersAsync();
        await admin.PostAsJsonAsync(
            $"/bands/{bandId}/events",
            new CreateEventRequest(EventType.Practice, CopenhagenToday.PlusDays(4), Confirmed: true),
            TestJson.Options);

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DigestJob>()
                .RunAtAsync(SystemClock.Instance.GetCurrentInstant(), default);
        }

        var recorder = factory.Services.GetRequiredService<RecordingEmailSender>();
        var digests = recorder.Sent
            .Where(mail => mail.Subject == $"Bandroom digest — {bandName}")
            .ToList();
        Assert.Equal(2, digests.Count);
        Assert.All(digests, mail =>
        {
            Assert.Contains($"Practice — {bandName}", mail.Body);
            Assert.Contains("availability", mail.Body);
        });
    }
}
