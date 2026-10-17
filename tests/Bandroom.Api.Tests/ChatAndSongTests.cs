using System.Net;
using System.Net.Http.Json;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Features.Chat;
using Bandroom.Api.Features.Comments;
using Bandroom.Api.Features.Events;
using Bandroom.Api.Features.Songs;
using Bandroom.Api.Tests.Support;
using NodaTime;
using Xunit;

namespace Bandroom.Api.Tests;

[Collection("api")]
public class ChatAndSongTests(ApiFactory factory)
{
    private async Task<(HttpClient Client, Guid BandId)> BandAsync()
    {
        var client = await factory.RegisterUserAsync("Lasse");
        var created = await (await client.PostAsJsonAsync(
                "/bands", new CreateBandRequest($"Band-{Guid.NewGuid():N}", null), TestJson.Options))
            .ReadAsAsync<BandDetailResponse>();
        return (client, created.Band.Id);
    }

    [Fact]
    public async Task NewBand_HasAGeneralChannel_AndMessagingWorks()
    {
        var (client, bandId) = await BandAsync();

        var channels = await (await client.GetAsync($"/bands/{bandId}/channels"))
            .ReadAsAsync<List<ChannelResponse>>();
        var general = Assert.Single(channels);
        Assert.Equal("general", general.Name);
        Assert.True(general.IsDefault);

        for (var i = 1; i <= 3; i++)
        {
            var sent = await client.PostAsJsonAsync(
                $"/bands/{bandId}/channels/{general.Id}/messages",
                new SendMessageRequest($"message {i}"), TestJson.Options);
            Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        }

        var page = await (await client.GetAsync($"/bands/{bandId}/channels/{general.Id}/messages"))
            .ReadAsAsync<MessagesPage>();
        Assert.Equal(3, page.Items.Count);
        Assert.Equal("message 1", page.Items[0].Body);   // chronological
        Assert.Equal("Lasse", page.Items[0].AuthorName);
        Assert.Null(page.NextBefore);
    }

    [Fact]
    public async Task Messages_PageBackwardsWithTheCursor()
    {
        var (client, bandId) = await BandAsync();
        var general = (await (await client.GetAsync($"/bands/{bandId}/channels"))
            .ReadAsAsync<List<ChannelResponse>>())[0];

        for (var i = 1; i <= 5; i++)
        {
            await client.PostAsJsonAsync(
                $"/bands/{bandId}/channels/{general.Id}/messages",
                new SendMessageRequest($"m{i}"), TestJson.Options);
        }

        var latest = await (await client.GetAsync(
                $"/bands/{bandId}/channels/{general.Id}/messages?take=2"))
            .ReadAsAsync<MessagesPage>();
        Assert.Equal(["m4", "m5"], latest.Items.Select(m => m.Body));
        Assert.NotNull(latest.NextBefore);

        var older = await (await client.GetAsync(
                $"/bands/{bandId}/channels/{general.Id}/messages?take=2&before={Uri.EscapeDataString(latest.NextBefore!)}"))
            .ReadAsAsync<MessagesPage>();
        Assert.Equal(["m2", "m3"], older.Items.Select(m => m.Body));
    }

    [Fact]
    public async Task Songs_CrudRoundTrip()
    {
        var (client, bandId) = await BandAsync();

        var created = await (await client.PostAsJsonAsync(
                $"/bands/{bandId}/songs",
                new CreateSongRequest("Silverline", "F#m", 132, null), TestJson.Options))
            .ReadAsAsync<SongResponse>();
        Assert.Equal(132, created.Bpm);

        var updated = await (await client.PatchAsJsonAsync(
                $"/bands/{bandId}/songs/{created.Id}",
                new UpdateSongRequest(null, null, 128, SongStatus.Active, "capo 2"), TestJson.Options))
            .ReadAsAsync<SongResponse>();
        Assert.Equal(128, updated.Bpm);
        Assert.Equal("capo 2", updated.Notes);

        var list = await (await client.GetAsync($"/bands/{bandId}/songs")).ReadAsAsync<List<SongResponse>>();
        Assert.Single(list);
    }

    [Fact]
    public async Task EventComments_AttachAndList()
    {
        var (client, bandId) = await BandAsync();
        var date = LocalDate.FromDateTime(DateTime.UtcNow.Date).PlusDays(10);
        var evt = await (await client.PostAsJsonAsync(
                $"/bands/{bandId}/events",
                new CreateEventRequest(EventType.Practice, date), TestJson.Options))
            .ReadAsAsync<EventDetailResponse>();

        var comment = await client.PostAsJsonAsync(
            $"/bands/{bandId}/comments",
            new CreateCommentRequest(CommentTarget.Event, evt.Event.Id, "bringing the new pedal"), TestJson.Options);
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);

        // Timestamps are demo-version-only.
        var bad = await client.PostAsJsonAsync(
            $"/bands/{bandId}/comments",
            new CreateCommentRequest(CommentTarget.Event, evt.Event.Id, "at 1:23", 83), TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var comments = await (await client.GetAsync(
                $"/bands/{bandId}/comments?targetType=event&targetId={evt.Event.Id}"))
            .ReadAsAsync<List<CommentResponse>>();
        var only = Assert.Single(comments);
        Assert.Equal("bringing the new pedal", only.Body);
        Assert.Equal("Lasse", only.AuthorName);
    }

    [Fact]
    public async Task Songs_CarryDurationChordsAndFire_AndTheRepertoireCanSortByFire()
    {
        var (client, bandId) = await BandAsync();
        var song = await (await client.PostAsJsonAsync($"/bands/{bandId}/songs",
                new CreateSongRequest("Hold the line", "Am", 120, null, 215, "[Am]Hold the [F]line"), TestJson.Options))
            .ReadAsAsync<SongResponse>();
        Assert.Equal(215, song.DurationSeconds);
        Assert.Equal("[Am]Hold the [F]line", song.Chords);
        Assert.Equal(0, song.FireCount);

        var lit = await (await client.PutAsync($"/bands/{bandId}/songs/{song.Id}/fire", null)).ReadAsAsync<SongFireResponse>();
        Assert.Equal((1, true), (lit.FireCount, lit.FiredByMe));
        lit = await (await client.PutAsync($"/bands/{bandId}/songs/{song.Id}/fire", null)).ReadAsAsync<SongFireResponse>();
        Assert.Equal(1, lit.FireCount); // idempotent

        var list = await (await client.GetAsync($"/bands/{bandId}/songs")).ReadAsAsync<List<SongResponse>>();
        Assert.True(list.Single().FiredByMe);

        var cleared = await (await client.PatchAsJsonAsync($"/bands/{bandId}/songs/{song.Id}",
                new UpdateSongRequest(null, null, null, null, null, 0, ""), TestJson.Options))
            .ReadAsAsync<SongResponse>();
        Assert.Null(cleared.DurationSeconds);
        Assert.Null(cleared.Chords);
        Assert.Equal(1, cleared.FireCount);

        var unlit = await client.DeleteAsync($"/bands/{bandId}/songs/{song.Id}/fire");
        Assert.Equal((0, false), ((await unlit.ReadAsAsync<SongFireResponse>()).FireCount, false));

        var tooLong = await client.PostAsJsonAsync($"/bands/{bandId}/songs",
            new CreateSongRequest("Long", null, null, null, 4 * 60 * 60), TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Gig_HasAContact_ASetlistWithATotal_AndWhoBringsWhat()
    {
        var (client, bandId) = await BandAsync();
        var me = (await (await client.GetAsync($"/bands/{bandId}")).ReadAsAsync<BandDetailResponse>()).Members.Single();
        var date = SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb["Europe/Copenhagen"]).Date.PlusDays(30);

        var gig = await (await client.PostAsJsonAsync($"/bands/{bandId}/events",
                new CreateEventRequest(EventType.Gig, date, null, new LocalTime(21, 0), null, "Loppen", "Christiania",
                    ContactName: "Mette", ContactPhone: "+45 12 34 56 78"), TestJson.Options))
            .ReadAsAsync<EventDetailResponse>();
        Assert.Equal("Mette", gig.Contact.Name);
        Assert.Null(gig.Contact.Email);
        var id = gig.Event.Id;

        // Contact can be edited later by the creator; a bad email is refused.
        var bad = await client.PatchAsJsonAsync($"/bands/{bandId}/events/{id}", new UpdateEventRequest(ContactEmail: "nope"), TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var edited = await (await client.PatchAsJsonAsync($"/bands/{bandId}/events/{id}",
            new UpdateEventRequest(ContactEmail: "mette@loppen.dk", ContactPhone: ""), TestJson.Options)).ReadAsAsync<EventDetailResponse>();
        Assert.Equal("mette@loppen.dk", edited.Contact.Email);
        Assert.Null(edited.Contact.Phone);

        // Setlist: ordered songs from the repertoire, total from durations.
        var a = await (await client.PostAsJsonAsync($"/bands/{bandId}/songs", new CreateSongRequest("A", null, null, null, 200), TestJson.Options)).ReadAsAsync<SongResponse>();
        var b = await (await client.PostAsJsonAsync($"/bands/{bandId}/songs", new CreateSongRequest("B", null, null, null, 100), TestJson.Options)).ReadAsAsync<SongResponse>();
        var c = await (await client.PostAsJsonAsync($"/bands/{bandId}/songs", new CreateSongRequest("C"), TestJson.Options)).ReadAsAsync<SongResponse>();
        var withSet = await (await client.PutAsJsonAsync($"/bands/{bandId}/events/{id}/setlist",
            new ReplaceSetlistRequest([b.Id, a.Id, c.Id]), TestJson.Options)).ReadAsAsync<EventDetailResponse>();
        Assert.Equal(["B", "A", "C"], withSet.Setlist.Select(s => s.Title));
        Assert.Equal(300, withSet.SetlistSeconds);
        var reordered = await (await client.PutAsJsonAsync($"/bands/{bandId}/events/{id}/setlist",
            new ReplaceSetlistRequest([a.Id]), TestJson.Options)).ReadAsAsync<EventDetailResponse>();
        Assert.Equal(["A"], reordered.Setlist.Select(s => s.Title));
        var foreign = await client.PutAsJsonAsync($"/bands/{bandId}/events/{id}/setlist", new ReplaceSetlistRequest([Guid.NewGuid()]), TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);

        // Who brings what: a line, a name, a tick.
        var withItem = await (await client.PostAsJsonAsync($"/bands/{bandId}/events/{id}/items",
            new CreateEventItemRequest("small amp"), TestJson.Options)).ReadAsAsync<EventDetailResponse>();
        var item = Assert.Single(withItem.Items);
        Assert.Null(item.AssigneeName);
        var claimed = await (await client.PatchAsJsonAsync($"/bands/{bandId}/events/{id}/items/{item.Id}",
            new UpdateEventItemRequest(AssigneeMembershipId: me.MembershipId, Done: true), TestJson.Options)).ReadAsAsync<EventDetailResponse>();
        Assert.Equal(me.DisplayName, claimed.Items.Single().AssigneeName);
        Assert.True(claimed.Items.Single().Done);
        var released = await (await client.PatchAsJsonAsync($"/bands/{bandId}/events/{id}/items/{item.Id}",
            new UpdateEventItemRequest(ClearAssignee: true), TestJson.Options)).ReadAsAsync<EventDetailResponse>();
        Assert.Null(released.Items.Single().AssigneeMembershipId);
        var gone = await (await client.DeleteAsync($"/bands/{bandId}/events/{id}/items/{item.Id}")).ReadAsAsync<EventDetailResponse>();
        Assert.Empty(gone.Items);

        // The detail shows all of it in one read.
        var detail = await (await client.GetAsync($"/bands/{bandId}/events/{id}")).ReadAsAsync<EventDetailResponse>();
        Assert.Equal("Mette", detail.Contact.Name);
        Assert.Single(detail.Setlist);
    }
}
