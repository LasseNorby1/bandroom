using System.Net.Http.Json;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Availability;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Features.Events;
using Bandroom.Api.Features.Scheduling;
using Bandroom.Api.Tests.Support;
using Bandroom.Domain.Scheduling;
using NodaTime;
using Xunit;

namespace Bandroom.Api.Tests;

[Collection("api")]
public class FinderTests(ApiFactory factory)
{
    private static readonly WeeklySlot[] AllEvenings = Enum.GetValues<IsoDayOfWeek>()
        .Where(day => day != IsoDayOfWeek.None)
        .Select(day => new WeeklySlot(day, PracticeSlot.Evening))
        .ToArray();

    private async Task<(HttpClient Admin, List<HttpClient> Members, Guid BandId)> FourPieceBandAsync()
    {
        var admin = await factory.RegisterUserAsync("Lasse");
        var created = await (await admin.PostAsJsonAsync(
                "/bands", new CreateBandRequest("Silverline", null), TestJson.Options))
            .ReadAsAsync<BandDetailResponse>();
        var bandId = created.Band.Id;

        var invite = await (await admin.PostAsJsonAsync(
                $"/bands/{bandId}/invites", new CreateInviteRequest(null, null), TestJson.Options))
            .ReadAsAsync<InviteCreatedResponse>();

        var members = new List<HttpClient>();
        foreach (var name in new[] { "Mikkel", "Sofie", "Jonas" })
        {
            var member = await factory.RegisterUserAsync(name);
            (await member.PostAsync($"/invites/{invite.Token}/accept", null)).EnsureSuccessStatusCode();
            members.Add(member);
        }

        return (admin, members, bandId);
    }

    [Fact]
    public async Task Finder_RanksTheFirstDayEveryoneIsFree_First()
    {
        var (admin, members, bandId) = await FourPieceBandAsync();

        foreach (var client in new[] { admin }.Concat(members))
        {
            await client.PutAsJsonAsync(
                $"/bands/{bandId}/availability/me/pattern", new UpdatePatternRequest(AllEvenings), TestJson.Options);
        }

        // The finder window starts tomorrow; block Lasse for the first week so the
        // earliest full-band day is knowably one week out.
        var probe = await (await admin.GetAsync($"/bands/{bandId}/practice-finder?days=21"))
            .ReadAsAsync<PracticeFinderResponse>();
        var windowStart = probe.From;
        var target = windowStart.PlusDays(7);
        await admin.PostAsJsonAsync(
            $"/bands/{bandId}/availability/me/exceptions",
            new CreateExceptionRequest(windowStart, target.PlusDays(-1), "tour"), TestJson.Options);

        var result = await (await admin.GetAsync($"/bands/{bandId}/practice-finder?days=21"))
            .ReadAsAsync<PracticeFinderResponse>();

        var top = result.Candidates[0];
        Assert.Equal(target, top.Date);
        Assert.Equal(4, top.AvailableCount);
        Assert.True(top.EveryoneAvailable);
        Assert.Contains(top.AvailableMembers, m => m.DisplayName == "Lasse");

        // Days inside Lasse's blockout still rank (3/4) but below the 4/4 days,
        // and Lasse is not among the free members there.
        var during = Assert.Single(result.Candidates, c => c.Date == windowStart);
        Assert.Equal(3, during.AvailableCount);
        Assert.DoesNotContain(during.AvailableMembers, m => m.DisplayName == "Lasse");
        Assert.True(result.Candidates.ToList().IndexOf(during) > 0);
    }

    [Fact]
    public async Task Finder_QuorumFiltersOutThinDays()
    {
        var (admin, members, bandId) = await FourPieceBandAsync();
        foreach (var client in new[] { admin }.Concat(members))
        {
            await client.PutAsJsonAsync(
                $"/bands/{bandId}/availability/me/pattern", new UpdatePatternRequest(AllEvenings), TestJson.Options);
        }

        await admin.PatchAsJsonAsync(
            $"/bands/{bandId}", new UpdateBandRequest(null, null, 4, null, null), TestJson.Options);

        var probe = await (await admin.GetAsync($"/bands/{bandId}/practice-finder?days=14"))
            .ReadAsAsync<PracticeFinderResponse>();
        var blockDay = probe.From.PlusDays(2);
        await admin.PostAsJsonAsync(
            $"/bands/{bandId}/availability/me/exceptions",
            new CreateExceptionRequest(blockDay, blockDay, null), TestJson.Options);

        var result = await (await admin.GetAsync($"/bands/{bandId}/practice-finder?days=14"))
            .ReadAsAsync<PracticeFinderResponse>();

        Assert.Equal(4, result.Quorum);
        Assert.All(result.Candidates, candidate => Assert.True(candidate.EveryoneAvailable));
        Assert.DoesNotContain(result.Candidates, candidate => candidate.Date == blockDay);
    }

    [Fact]
    public async Task Finder_SlotsAreIndependent()
    {
        var (admin, _, bandId) = await FourPieceBandAsync();
        await admin.PutAsJsonAsync(
            $"/bands/{bandId}/availability/me/pattern", new UpdatePatternRequest(AllEvenings), TestJson.Options);

        var afternoons = await (await admin.GetAsync($"/bands/{bandId}/practice-finder?days=14&slot=afternoon"))
            .ReadAsAsync<PracticeFinderResponse>();
        Assert.Empty(afternoons.Candidates);
    }

    [Fact]
    public async Task Finder_SkipsDaysThatAlreadyHaveAPractice()
    {
        var (admin, members, bandId) = await FourPieceBandAsync();
        foreach (var client in new[] { admin }.Concat(members))
        {
            await client.PutAsJsonAsync(
                $"/bands/{bandId}/availability/me/pattern", new UpdatePatternRequest(AllEvenings), TestJson.Options);
        }

        var before = await (await admin.GetAsync($"/bands/{bandId}/practice-finder?days=14"))
            .ReadAsAsync<PracticeFinderResponse>();
        var target = before.Candidates[0].Date;

        var created = await (await admin.PostAsJsonAsync(
                $"/bands/{bandId}/events",
                new CreateEventRequest(EventType.Practice, target, Confirmed: false), TestJson.Options))
            .ReadAsAsync<EventDetailResponse>();

        var after = await (await admin.GetAsync($"/bands/{bandId}/practice-finder?days=14"))
            .ReadAsAsync<PracticeFinderResponse>();
        Assert.DoesNotContain(after.Candidates, candidate => candidate.Date == target);

        // Cancelling the practice frees the day again.
        (await admin.PostAsync($"/bands/{bandId}/events/{created.Event.Id}/cancel", null))
            .EnsureSuccessStatusCode();
        var freed = await (await admin.GetAsync($"/bands/{bandId}/practice-finder?days=14"))
            .ReadAsAsync<PracticeFinderResponse>();
        Assert.Contains(freed.Candidates, candidate => candidate.Date == target);
    }

    [Fact]
    public async Task Finder_EmptyPatternsYieldNothing()
    {
        var (admin, _, bandId) = await FourPieceBandAsync();

        var result = await (await admin.GetAsync($"/bands/{bandId}/practice-finder"))
            .ReadAsAsync<PracticeFinderResponse>();
        Assert.Empty(result.Candidates);
    }
}
