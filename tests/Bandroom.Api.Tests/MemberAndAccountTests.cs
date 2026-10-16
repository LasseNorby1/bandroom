using System.Net;
using System.Net.Http.Json;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Auth;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Features.Events;
using Bandroom.Api.Features.Scheduling;
using Bandroom.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Xunit;

namespace Bandroom.Api.Tests;

[Collection("api")]
public class MemberAndAccountTests(ApiFactory factory)
{
    private static LocalDate Soon(int days) =>
        SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb["Europe/Copenhagen"]).Date.PlusDays(days);

    private async Task<(HttpClient Admin, HttpClient Member, BandDetailResponse Band, Guid MemberMembershipId)> BandWithTwoAsync()
    {
        var admin = await factory.RegisterUserAsync("Admin");
        var band = await (await admin.PostAsJsonAsync("/bands", new CreateBandRequest("Two", null), TestJson.Options))
            .ReadAsAsync<BandDetailResponse>();
        var invite = await (await admin.PostAsJsonAsync(
                $"/bands/{band.Band.Id}/invites", new CreateInviteRequest(null, null), TestJson.Options))
            .ReadAsAsync<InviteCreatedResponse>();
        var member = await factory.RegisterUserAsync("Member");
        var accepted = await (await member.PostAsync($"/invites/{invite.Token}/accept", null))
            .ReadAsAsync<AcceptInviteResponse>();
        return (admin, member, band, accepted.MembershipId);
    }

    [Fact]
    public async Task Leave_RemovesAccess_KeepsHistory_AndRejoinRevives()
    {
        var (admin, member, band, membershipId) = await BandWithTwoAsync();
        var bandId = band.Band.Id;

        // The member proposes a practice (history that must survive their departure).
        var evt = await (await member.PostAsJsonAsync(
                $"/bands/{bandId}/events",
                new CreateEventRequest(EventType.Practice, Soon(10)), TestJson.Options))
            .ReadAsAsync<EventDetailResponse>();

        var leave = await member.DeleteAsync($"/bands/{bandId}/members/me");
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);

        // Gone for them, not for the band.
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/bands/{bandId}")).StatusCode);
        Assert.Empty(await (await member.GetAsync("/bands")).ReadAsAsync<List<BandSummaryResponse>>());
        var detail = await (await admin.GetAsync($"/bands/{bandId}")).ReadAsAsync<BandDetailResponse>();
        Assert.Single(detail.Members);
        var stillThere = await (await admin.GetAsync($"/bands/{bandId}/events/{evt.Event.Id}")).ReadAsAsync<EventDetailResponse>();
        Assert.Equal(0, stillThere.Event.GoingCount); // their upcoming rsvp no longer counts
        Assert.Equal(membershipId, stillThere.Event.CreatedByMembershipId);

        // Rejoining through a fresh invite revives the same membership.
        var invite = await (await admin.PostAsJsonAsync(
                $"/bands/{bandId}/invites", new CreateInviteRequest(null, null), TestJson.Options))
            .ReadAsAsync<InviteCreatedResponse>();
        var back = await (await member.PostAsync($"/invites/{invite.Token}/accept", null)).ReadAsAsync<AcceptInviteResponse>();
        Assert.Equal(membershipId, back.MembershipId);
        Assert.Equal(2, (await (await admin.GetAsync($"/bands/{bandId}")).ReadAsAsync<BandDetailResponse>()).Members.Count);
    }

    [Fact]
    public async Task Admin_CanRemoveAndPromote_LastAdminIsProtected()
    {
        var (admin, member, band, membershipId) = await BandWithTwoAsync();
        var bandId = band.Band.Id;
        var adminMembershipId = band.Members.Single().MembershipId;

        // Members can't remove anyone.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.DeleteAsync($"/bands/{bandId}/members/{adminMembershipId}")).StatusCode);

        // The only admin can't demote themselves…
        var demote = await admin.PatchAsJsonAsync(
            $"/bands/{bandId}/members/{adminMembershipId}", new UpdateMemberRequest(BandRole.Member), TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, demote.StatusCode);

        // …but can promote someone and then step down.
        var promoted = await (await admin.PatchAsJsonAsync(
                $"/bands/{bandId}/members/{membershipId}", new UpdateMemberRequest(BandRole.Admin), TestJson.Options))
            .ReadAsAsync<MemberResponse>();
        Assert.Equal(BandRole.Admin, promoted.Role);

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/bands/{bandId}/members/{membershipId}")).StatusCode);
        Assert.Single((await (await admin.GetAsync($"/bands/{bandId}")).ReadAsAsync<BandDetailResponse>()).Members);
    }

    [Fact]
    public async Task LastAdminLeaving_PromotesTheLongestStandingMember()
    {
        var (admin, member, band, membershipId) = await BandWithTwoAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/bands/{band.Band.Id}/members/me")).StatusCode);
        var detail = await (await member.GetAsync($"/bands/{band.Band.Id}")).ReadAsAsync<BandDetailResponse>();
        var only = Assert.Single(detail.Members);
        Assert.Equal(membershipId, only.MembershipId);
        Assert.Equal(BandRole.Admin, only.Role);
    }

    [Fact]
    public async Task ChangePassword_SignsOutEverySession()
    {
        var client = factory.CreateClient();
        var email = $"pw-{Guid.NewGuid():N}@example.dk";
        var pair = await (await client.PostAsJsonAsync("/auth/register",
                new RegisterRequest(email, "correct horse battery", "Pw"), TestJson.Options))
            .ReadAsAsync<TokenPairResponse>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", pair.AccessToken);

        var wrong = await client.PostAsJsonAsync("/auth/change-password",
            new ChangePasswordRequest("nope nope nope", "new password here"), TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var ok = await client.PostAsJsonAsync("/auth/change-password",
            new ChangePasswordRequest("correct horse battery", "new password here"), TestJson.Options);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        // The old refresh token is dead; the new password works.
        var refresh = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(pair.RefreshToken), TestJson.Options);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "new password here"), TestJson.Options);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_AnonymisesAndLeavesBands_ExportComesFirst()
    {
        var (admin, member, band, membershipId) = await BandWithTwoAsync();
        var bandId = band.Band.Id;
        await member.PostAsJsonAsync($"/bands/{bandId}/comments",
            new Bandroom.Api.Features.Comments.CreateCommentRequest(CommentTarget.Event,
                (await (await admin.PostAsJsonAsync($"/bands/{bandId}/events",
                    new CreateEventRequest(EventType.Gig, Soon(20)), TestJson.Options))
                    .ReadAsAsync<EventDetailResponse>()).Event.Id, "I'll bring cables"), TestJson.Options);

        var export = await (await member.GetAsync("/auth/me/export")).ReadAsAsync<AccountExport>();
        Assert.Equal("Member", export.DisplayName);
        Assert.Single(export.Bands);
        Assert.Single(export.Bands[0].Comments);

        var wrong = await member.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/auth/me")
        {
            Content = JsonContent.Create(new DeleteAccountRequest("wrong"), options: TestJson.Options),
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var deleted = await member.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/auth/me")
        {
            Content = JsonContent.Create(new DeleteAccountRequest("correct horse battery"), options: TestJson.Options),
        });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // The band lost a member but kept the comment, now by "Deleted member".
        var detail = await (await admin.GetAsync($"/bands/{bandId}")).ReadAsAsync<BandDetailResponse>();
        Assert.Single(detail.Members);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var m = await db.Memberships.IgnoreQueryFilters().SingleAsync(x => x.Id == membershipId);
        var user = await db.Users.SingleAsync(u => u.Id == m.UserId);
        Assert.NotNull(m.LeftAt);
        Assert.Equal("Deleted member", user.DisplayName);
        Assert.EndsWith("@bandroom.invalid", user.Email);
        Assert.Equal(1, await db.Comments.IgnoreQueryFilters().CountAsync(c => c.AuthorMembershipId == membershipId));
    }

    [Fact]
    public async Task LateCant_BelowQuorum_WarnsTheOrganiserOnce()
    {
        var (admin, member, band, _) = await BandWithTwoAsync();
        var bandId = band.Band.Id;
        // Quorum 2 of 2: both going confirms; one dropping out warns.
        var evt = await (await admin.PostAsJsonAsync($"/bands/{bandId}/events",
                new CreateEventRequest(EventType.Practice, Soon(30)), TestJson.Options))
            .ReadAsAsync<EventDetailResponse>();
        var confirmed = await (await member.PostAsJsonAsync($"/bands/{bandId}/events/{evt.Event.Id}/rsvp",
                new RsvpRequest(RsvpStatus.Going), TestJson.Options)).ReadAsAsync<EventDetailResponse>();
        Assert.Equal(EventStatus.Confirmed, confirmed.Event.Status);
        Assert.False(confirmed.Event.BelowQuorum);

        var recorder = factory.Services.GetRequiredService<RecordingEmailSender>();
        var before = recorder.Sent.Count(mail => mail.Subject.StartsWith("Below quorum"));

        var dropped = await (await member.PostAsJsonAsync($"/bands/{bandId}/events/{evt.Event.Id}/rsvp",
                new RsvpRequest(RsvpStatus.NotGoing, "sick"), TestJson.Options)).ReadAsAsync<EventDetailResponse>();
        Assert.Equal(EventStatus.Confirmed, dropped.Event.Status); // never auto-cancelled
        Assert.True(dropped.Event.BelowQuorum);
        Assert.Equal(before + 1, recorder.Sent.Count(mail => mail.Subject.StartsWith("Below quorum")));

        // Flapping doesn't spam: maybe → still below, no second mail.
        await member.PostAsJsonAsync($"/bands/{bandId}/events/{evt.Event.Id}/rsvp", new RsvpRequest(RsvpStatus.Maybe), TestJson.Options);
        Assert.Equal(before + 1, recorder.Sent.Count(mail => mail.Subject.StartsWith("Below quorum")));
    }

    [Fact]
    public async Task Finder_ReportsPatternStaleness()
    {
        var (admin, _, band, _) = await BandWithTwoAsync();
        var finder = await (await admin.GetAsync($"/bands/{band.Band.Id}/practice-finder")).ReadAsAsync<PracticeFinderResponse>();
        Assert.Equal(2, finder.Members.Count);
        Assert.All(finder.Members, m => Assert.Null(m.PatternUpdatedAt));
        Assert.All(finder.Members, m => Assert.Equal(0, m.OpenSlots));
    }
}
