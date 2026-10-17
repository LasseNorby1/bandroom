using System.Net;
using System.Net.Http.Json;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Features.Events;
using Bandroom.Api.Tests.Support;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using NodaTime;
using Xunit;

namespace Bandroom.Api.Tests;

[Collection("api")]
public class EventTests(ApiFactory factory)
{
    private static LocalDate NextMonth => LocalDate.FromDateTime(DateTime.UtcNow.Date).PlusDays(30);

    private async Task<(HttpClient Admin, string AdminToken, HttpClient Member, Guid BandId)> BandWithTwoMembersAsync()
    {
        var (admin, adminToken) = await factory.RegisterUserWithTokenAsync("Lasse");
        var created = await (await admin.PostAsJsonAsync(
                "/bands", new CreateBandRequest("Band", null), TestJson.Options))
            .ReadAsAsync<BandDetailResponse>();

        var invite = await (await admin.PostAsJsonAsync(
                $"/bands/{created.Band.Id}/invites", new CreateInviteRequest(null, null), TestJson.Options))
            .ReadAsAsync<InviteCreatedResponse>();
        var member = await factory.RegisterUserAsync("Sofie");
        (await member.PostAsync($"/invites/{invite.Token}/accept", null)).EnsureSuccessStatusCode();

        return (admin, adminToken, member, created.Band.Id);
    }

    private static async Task<EventDetailResponse> CreatePracticeAsync(
        HttpClient client, Guid bandId, LocalDate date)
    {
        var response = await client.PostAsJsonAsync(
            $"/bands/{bandId}/events",
            new CreateEventRequest(EventType.Practice, date), TestJson.Options);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsAsync<EventDetailResponse>();
    }

    [Fact]
    public async Task CreatePractice_IsProposedWithCreatorGoing_AndDefaultSlot()
    {
        var (admin, _, _, bandId) = await BandWithTwoMembersAsync();

        var detail = await CreatePracticeAsync(admin, bandId, NextMonth);

        Assert.Equal(EventStatus.Proposed, detail.Event.Status);
        Assert.Equal(Bandroom.Domain.Scheduling.PracticeSlot.Evening, detail.Event.Slot);
        var rsvp = Assert.Single(detail.Rsvps);
        Assert.Equal(RsvpStatus.Going, rsvp.Status);
        Assert.Equal("Lasse", rsvp.DisplayName);
    }

    [Fact]
    public async Task Rsvp_ReachingQuorum_AutoConfirms()
    {
        var (admin, _, member, bandId) = await BandWithTwoMembersAsync();

        // Default quorum is 2: creator's auto-going is 1 of 2.
        var detail = await CreatePracticeAsync(admin, bandId, NextMonth);
        Assert.Equal(EventStatus.Proposed, detail.Event.Status);

        var after = await (await member.PostAsJsonAsync(
                $"/bands/{bandId}/events/{detail.Event.Id}/rsvp",
                new RsvpRequest(RsvpStatus.Going), TestJson.Options))
            .ReadAsAsync<EventDetailResponse>();

        Assert.Equal(EventStatus.Confirmed, after.Event.Status);
        Assert.Equal(2, after.Event.GoingCount);
    }

    [Fact]
    public async Task Rsvp_IsAnUpsert()
    {
        var (admin, _, member, bandId) = await BandWithTwoMembersAsync();
        var detail = await CreatePracticeAsync(admin, bandId, NextMonth);

        await member.PostAsJsonAsync(
            $"/bands/{bandId}/events/{detail.Event.Id}/rsvp",
            new RsvpRequest(RsvpStatus.Maybe, "depends on work"), TestJson.Options);
        var final = await (await member.PostAsJsonAsync(
                $"/bands/{bandId}/events/{detail.Event.Id}/rsvp",
                new RsvpRequest(RsvpStatus.NotGoing, "can't after all"), TestJson.Options))
            .ReadAsAsync<EventDetailResponse>();

        Assert.Equal(2, final.Rsvps.Count);
        var sofie = Assert.Single(final.Rsvps, r => r.DisplayName == "Sofie");
        Assert.Equal(RsvpStatus.NotGoing, sofie.Status);
        Assert.Equal("can't after all", sofie.Note);
    }

    [Fact]
    public async Task Cancel_RequiresCreatorOrAdmin()
    {
        var (admin, _, member, bandId) = await BandWithTwoMembersAsync();
        var detail = await CreatePracticeAsync(admin, bandId, NextMonth);

        var forbidden = await member.PostAsync($"/bands/{bandId}/events/{detail.Event.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var cancelled = await (await admin.PostAsync($"/bands/{bandId}/events/{detail.Event.Id}/cancel", null))
            .ReadAsAsync<EventDetailResponse>();
        Assert.Equal(EventStatus.Cancelled, cancelled.Event.Status);
    }

    [Fact]
    public async Task List_ReturnsUpcomingInOrder()
    {
        var (admin, _, _, bandId) = await BandWithTwoMembersAsync();
        await CreatePracticeAsync(admin, bandId, NextMonth.PlusDays(7));
        await CreatePracticeAsync(admin, bandId, NextMonth);
        await admin.PostAsJsonAsync(
            $"/bands/{bandId}/events",
            new CreateEventRequest(EventType.Gig, NextMonth.PlusDays(14), null, new LocalTime(20, 0),
                new LocalTime(23, 0), "Release show", "Vega", Confirmed: true), TestJson.Options);

        var events = await (await admin.GetAsync($"/bands/{bandId}/events?days=90"))
            .ReadAsAsync<List<EventSummaryResponse>>();

        Assert.Equal(3, events.Count);
        Assert.Equal(NextMonth, events[0].Date);
        Assert.Equal("Release show", events[2].Title);
        Assert.Equal(EventStatus.Confirmed, events[2].Status);
    }

    [Fact]
    public async Task SignalR_MembersReceiveEventChanged()
    {
        var (admin, adminToken, _, bandId) = await BandWithTwoMembersAsync();

        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/band"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(adminToken);
            })
            .Build();

        var received = new TaskCompletionSource<EventChangedNotification>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<EventChangedNotification>(
            "eventChanged", notification => received.TrySetResult(notification));

        await connection.StartAsync();
        await connection.InvokeAsync("JoinBand", bandId);

        var detail = await CreatePracticeAsync(admin, bandId, NextMonth);

        var notification = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(detail.Event.Id, notification.EventId);
        Assert.Equal("created", notification.Action);
        Assert.Equal(bandId, notification.BandId);
    }

    [Fact]
    public async Task SignalR_OutsidersCannotJoinTheBandGroup()
    {
        var (_, _, _, bandId) = await BandWithTwoMembersAsync();
        var (_, outsiderToken) = await factory.RegisterUserWithTokenAsync("Impostor");

        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/band"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(outsiderToken);
            })
            .Build();
        await connection.StartAsync();

        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(
            () => connection.InvokeAsync("JoinBand", bandId));
    }

    [Fact]
    public async Task Presence_ListsWhoHasTheBandOpen_AndLastSeenIsStamped()
    {
        var (admin, adminToken, member, bandId) = await BandWithTwoMembersAsync();

        // Any band request stamps "last active"; the detail shows it per member.
        var detail = await (await admin.GetAsync($"/bands/{bandId}")).ReadAsAsync<BandDetailResponse>();
        var adminRow = detail.Members.Single(m => m.Role == BandRole.Admin);
        Assert.NotNull(adminRow.LastActiveAt);
        var memberRow = detail.Members.Single(m => m.Role == BandRole.Member);
        Assert.Null(memberRow.LastActiveAt); // the member hasn't looked yet
        await member.GetAsync($"/bands/{bandId}");
        detail = await (await admin.GetAsync($"/bands/{bandId}")).ReadAsAsync<BandDetailResponse>();
        Assert.NotNull(detail.Members.Single(m => m.Role == BandRole.Member).LastActiveAt);

        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/band"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(adminToken);
            })
            .Build();
        var changes = new List<PresenceChangedNotification>();
        connection.On<PresenceChangedNotification>("presenceChanged", n => { lock (changes) changes.Add(n); });

        await connection.StartAsync();
        await connection.InvokeAsync("JoinBand", bandId);
        var here = await connection.InvokeAsync<List<Guid>>("WhoIsHere", bandId);
        Assert.Equal([adminRow.UserId], here);

        // A second tab of the same person doesn't change the room; closing both does.
        await using var second = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/band"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(adminToken);
            })
            .Build();
        await second.StartAsync();
        await second.InvokeAsync("JoinBand", bandId);
        Assert.Equal([adminRow.UserId], await second.InvokeAsync<List<Guid>>("WhoIsHere", bandId));
        await second.StopAsync();
        await Task.Delay(200);
        Assert.Equal([adminRow.UserId], await connection.InvokeAsync<List<Guid>>("WhoIsHere", bandId));

        // Another band's room is not visible through this connection.
        Assert.Empty(await connection.InvokeAsync<List<Guid>>("WhoIsHere", Guid.NewGuid()));
    }
}
