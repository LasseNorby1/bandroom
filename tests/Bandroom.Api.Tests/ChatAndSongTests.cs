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
}
