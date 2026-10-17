using System.Net;
using System.Net.Http.Json;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Availability;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Tests.Support;
using Bandroom.Api.Features.Scheduling;
using Bandroom.Domain.Scheduling;
using NodaTime;
using Xunit;

namespace Bandroom.Api.Tests;

[Collection("api")]
public class AvailabilityTests(ApiFactory factory)
{
    private static readonly WeeklySlot[] WeekdayEvenings =
    [
        new(IsoDayOfWeek.Monday, PracticeSlot.Evening),
        new(IsoDayOfWeek.Tuesday, PracticeSlot.Evening),
        new(IsoDayOfWeek.Thursday, PracticeSlot.Evening),
    ];

    private async Task<(HttpClient Admin, HttpClient Member, Guid BandId)> BandWithTwoMembersAsync()
    {
        var admin = await factory.RegisterUserAsync("Lasse");
        var created = await (await admin.PostAsJsonAsync(
                "/bands", new CreateBandRequest("Band", null), TestJson.Options))
            .ReadAsAsync<BandDetailResponse>();

        var invite = await (await admin.PostAsJsonAsync(
                $"/bands/{created.Band.Id}/invites", new CreateInviteRequest(null, null), TestJson.Options))
            .ReadAsAsync<InviteCreatedResponse>();

        var member = await factory.RegisterUserAsync("Sofie");
        (await member.PostAsync($"/invites/{invite.Token}/accept", null)).EnsureSuccessStatusCode();

        return (admin, member, created.Band.Id);
    }

    [Fact]
    public async Task Pattern_RoundTrips_AndStampsUpdatedAt()
    {
        var (admin, _, bandId) = await BandWithTwoMembersAsync();

        var updated = await (await admin.PutAsJsonAsync(
                $"/bands/{bandId}/availability/me/pattern",
                new UpdatePatternRequest(WeekdayEvenings), TestJson.Options))
            .ReadAsAsync<MyAvailabilityResponse>();
        Assert.Equal(3, updated.OpenSlots.Count);
        Assert.NotNull(updated.PatternUpdatedAt);

        var mine = await (await admin.GetAsync($"/bands/{bandId}/availability/me"))
            .ReadAsAsync<MyAvailabilityResponse>();
        Assert.Contains(new WeeklySlot(IsoDayOfWeek.Thursday, PracticeSlot.Evening), mine.OpenSlots);
        Assert.StartsWith("/calendar/", mine.CalendarFeedPath);
    }

    [Fact]
    public async Task Exceptions_CreateListDelete()
    {
        var (admin, _, bandId) = await BandWithTwoMembersAsync();
        var nextMonth = LocalDate.FromDateTime(DateTime.UtcNow.Date).PlusDays(30);

        var created = await (await admin.PostAsJsonAsync(
                $"/bands/{bandId}/availability/me/exceptions",
                new CreateExceptionRequest(nextMonth, nextMonth.PlusDays(3), "travel"), TestJson.Options))
            .ReadAsAsync<ExceptionResponse>();
        Assert.Equal("travel", created.Note);

        var mine = await (await admin.GetAsync($"/bands/{bandId}/availability/me"))
            .ReadAsAsync<MyAvailabilityResponse>();
        Assert.Contains(mine.Exceptions, e => e.Id == created.Id);

        var delete = await admin.DeleteAsync($"/bands/{bandId}/availability/me/exceptions/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task Exceptions_CannotDeleteAnotherMembers()
    {
        var (admin, member, bandId) = await BandWithTwoMembersAsync();
        var nextMonth = LocalDate.FromDateTime(DateTime.UtcNow.Date).PlusDays(30);

        var created = await (await admin.PostAsJsonAsync(
                $"/bands/{bandId}/availability/me/exceptions",
                new CreateExceptionRequest(nextMonth, nextMonth, null), TestJson.Options))
            .ReadAsAsync<ExceptionResponse>();

        var attempt = await member.DeleteAsync($"/bands/{bandId}/availability/me/exceptions/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, attempt.StatusCode);
    }

    [Fact]
    public async Task Exceptions_RejectInvertedRange()
    {
        var (admin, _, bandId) = await BandWithTwoMembersAsync();
        var date = LocalDate.FromDateTime(DateTime.UtcNow.Date).PlusDays(10);

        var response = await admin.PostAsJsonAsync(
            $"/bands/{bandId}/availability/me/exceptions",
            new CreateExceptionRequest(date, date.PlusDays(-2), null), TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Overview_ShowsEveryMembersAvailability()
    {
        var (admin, member, bandId) = await BandWithTwoMembersAsync();
        await admin.PutAsJsonAsync(
            $"/bands/{bandId}/availability/me/pattern", new UpdatePatternRequest(WeekdayEvenings), TestJson.Options);
        var soon = LocalDate.FromDateTime(DateTime.UtcNow.Date).PlusDays(5);
        await member.PostAsJsonAsync(
            $"/bands/{bandId}/availability/me/exceptions",
            new CreateExceptionRequest(soon, soon.PlusDays(1), "gig with the other band"), TestJson.Options);

        var overview = await (await member.GetAsync($"/bands/{bandId}/availability?days=30"))
            .ReadAsAsync<BandAvailabilityResponse>();

        Assert.Equal(2, overview.Members.Count);
        var lasse = Assert.Single(overview.Members, m => m.DisplayName == "Lasse");
        Assert.Equal(3, lasse.OpenSlots.Count);
        var sofie = Assert.Single(overview.Members, m => m.DisplayName == "Sofie");
        Assert.Single(sofie.Exceptions);
    }

    [Fact]
    public async Task Pattern_CanAlternateEveryOtherWeek_AndTheFinderFollowsIt()
    {
        var (admin, other, bandId) = await BandWithTwoMembersAsync();
        var allEvenings = Enum.GetValues<IsoDayOfWeek>().Where(d => d != IsoDayOfWeek.None)
            .Select(d => new WeeklySlot(d, PracticeSlot.Evening)).ToList();
        await other.PutAsJsonAsync($"/bands/{bandId}/availability/me/pattern", new UpdatePatternRequest(allEvenings), TestJson.Options);

        // Anchor on this week's Monday: this week open every evening, next week nothing (the kids).
        var probe = await (await admin.GetAsync($"/bands/{bandId}/practice-finder?days=21")).ReadAsAsync<PracticeFinderResponse>();
        var today = probe.From.PlusDays(-1);
        var thisMonday = today.PlusDays(-((int)today.DayOfWeek - 1));

        var notMonday = await admin.PutAsJsonAsync($"/bands/{bandId}/availability/me/pattern",
            new UpdatePatternRequest(allEvenings, [], thisMonday.PlusDays(1)), TestJson.Options);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, notMonday.StatusCode);
        var noAnchor = await admin.PutAsJsonAsync($"/bands/{bandId}/availability/me/pattern",
            new UpdatePatternRequest(allEvenings, []), TestJson.Options);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, noAnchor.StatusCode);

        var saved = await (await admin.PutAsJsonAsync($"/bands/{bandId}/availability/me/pattern",
            new UpdatePatternRequest(allEvenings, [], thisMonday), TestJson.Options)).ReadAsAsync<MyAvailabilityResponse>();
        Assert.Equal(thisMonday, saved.AnchorMonday);
        Assert.Empty(saved.AlternateOpenSlots);
        Assert.Equal(7, saved.OpenSlots.Count);

        var overview = await (await admin.GetAsync($"/bands/{bandId}/availability?days=14")).ReadAsAsync<BandAvailabilityResponse>();
        Assert.Contains(overview.Members, m => m.AnchorMonday == thisMonday);

        var finder = await (await admin.GetAsync($"/bands/{bandId}/practice-finder?days=21")).ReadAsAsync<PracticeFinderResponse>();
        Assert.NotEmpty(finder.Candidates);
        foreach (var candidate in finder.Candidates)
        {
            var monday = candidate.Date.PlusDays(-((int)candidate.Date.DayOfWeek - 1));
            var weeksOut = Period.Between(thisMonday, monday, PeriodUnits.Weeks).Weeks;
            var expected = weeksOut % 2 == 0 ? 2 : 1;
            Assert.Equal(expected, candidate.AvailableCount);
        }

        // Back to the same every week: the alternate set and anchor are dropped.
        var plain = await (await admin.PutAsJsonAsync($"/bands/{bandId}/availability/me/pattern",
            new UpdatePatternRequest(allEvenings), TestJson.Options)).ReadAsAsync<MyAvailabilityResponse>();
        Assert.Null(plain.AnchorMonday);
        Assert.Empty(plain.AlternateOpenSlots);
    }
}
