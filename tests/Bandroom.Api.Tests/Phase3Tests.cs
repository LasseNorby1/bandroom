using System.Net;
using System.Net.Http.Json;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Features.Demos;
using Bandroom.Api.Features.Events;
using Bandroom.Api.Features.Jobs;
using Bandroom.Api.Features.Songs;
using Bandroom.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Xunit;

namespace Bandroom.Api.Tests;

[Collection("api")]
public class Phase3Tests(ApiFactory factory)
{
    private async Task<(HttpClient Client, Guid BandId)> BandAsync()
    {
        var client = await factory.RegisterUserAsync("Lasse");
        var band = await (await client.PostAsJsonAsync("/bands", new CreateBandRequest("Phase 3", null), TestJson.Options))
            .ReadAsAsync<BandDetailResponse>();
        return (client, band.Band.Id);
    }

    private static async Task PutAsync(string url, byte[] bytes, string contentType)
    {
        using var raw = new HttpClient();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        (await raw.PutAsync(url, content)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Fire_TogglesPerMember_AndShowsOnTheSong()
    {
        var (client, bandId) = await BandAsync();
        var song = await (await client.PostAsJsonAsync($"/bands/{bandId}/songs", new CreateSongRequest("Riff"), TestJson.Options))
            .ReadAsAsync<SongResponse>();
        var init = await (await client.PostAsJsonAsync($"/bands/{bandId}/songs/{song.Id}/versions/uploads",
                new InitUploadRequest("take.mp3", "audio/mpeg", 2048), TestJson.Options)).ReadAsAsync<InitUploadResponse>();
        await PutAsync(init.UploadUrl, new byte[2048], "audio/mpeg");
        var version = await (await client.PostAsJsonAsync($"/bands/{bandId}/versions/{init.VersionId}/confirm",
                new ConfirmUploadRequest(null), TestJson.Options)).ReadAsAsync<VersionResponse>();
        Assert.Equal(0, version.FireCount);

        var fired = await (await client.PutAsync($"/bands/{bandId}/versions/{version.Id}/fire", null)).ReadAsAsync<FireResponse>();
        Assert.Equal((1, true), (fired.FireCount, fired.FiredByMe));
        var again = await (await client.PutAsync($"/bands/{bandId}/versions/{version.Id}/fire", null)).ReadAsAsync<FireResponse>();
        Assert.Equal(1, again.FireCount); // idempotent

        var detail = await (await client.GetAsync($"/bands/{bandId}/songs/{song.Id}")).ReadAsAsync<SongDetailResponse>();
        Assert.True(detail.Versions.Single().FiredByMe);
        Assert.Null(detail.Versions.Single().Bpm); // analysis is queued, never inline

        var unfired = await (await client.DeleteAsync($"/bands/{bandId}/versions/{version.Id}/fire")).ReadAsAsync<FireResponse>();
        Assert.Equal((0, false), (unfired.FireCount, unfired.FiredByMe));

        // The analysis row exists, queued, keyed by model version.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var analysis = await db.VersionAnalyses.IgnoreQueryFilters().SingleAsync(a => a.VersionId == version.Id);
        Assert.Equal(AnalysisStatus.Queued, analysis.Status);
        Assert.Equal(AnalysisJobRunner.ModelVersion, analysis.ModelVersion);
    }

    [Fact]
    public async Task Notice_IsPinnedByAdmins_AndClearedByEmptyText()
    {
        var (client, bandId) = await BandAsync();
        var set = await (await client.PutAsJsonAsync($"/bands/{bandId}/notice", new UpdateNoticeRequest("Door code 4471"), TestJson.Options))
            .ReadAsAsync<BandResponse>();
        Assert.Equal("Door code 4471", set.Notice);
        Assert.NotNull(set.NoticeUpdatedAt);

        var detail = await (await client.GetAsync($"/bands/{bandId}")).ReadAsAsync<BandDetailResponse>();
        Assert.Equal("Door code 4471", detail.Band.Notice);

        var cleared = await (await client.PutAsJsonAsync($"/bands/{bandId}/notice", new UpdateNoticeRequest("  "), TestJson.Options))
            .ReadAsAsync<BandResponse>();
        Assert.Null(cleared.Notice);
        Assert.Null(cleared.NoticeUpdatedAt);
    }

    [Fact]
    public async Task SongFiles_UploadListDownloadDelete_WithStorageAccounting()
    {
        var (client, bandId) = await BandAsync();
        var song = await (await client.PostAsJsonAsync($"/bands/{bandId}/songs", new CreateSongRequest("Silverline"), TestJson.Options))
            .ReadAsAsync<SongResponse>();

        var rejected = await client.PostAsJsonAsync($"/bands/{bandId}/songs/{song.Id}/files/uploads",
            new InitSongFileUploadRequest("Chords", "chords.exe", "application/octet-stream", 100), TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var init = await (await client.PostAsJsonAsync($"/bands/{bandId}/songs/{song.Id}/files/uploads",
                new InitSongFileUploadRequest("Chords", "chords.pdf", "application/pdf", 4096), TestJson.Options))
            .ReadAsAsync<InitSongFileUploadResponse>();
        var bytes = new byte[4096];
        await PutAsync(init.UploadUrl, bytes, "application/pdf");
        var file = await (await client.PostAsync($"/bands/{bandId}/song-files/{init.FileId}/confirm", null)).ReadAsAsync<SongFileResponse>();
        Assert.Equal(4096, file.SizeBytes);
        Assert.Equal("Lasse", file.UploaderName);

        var listed = await (await client.GetAsync($"/bands/{bandId}/songs/{song.Id}/files")).ReadAsAsync<List<SongFileResponse>>();
        Assert.Single(listed);
        var band = await (await client.GetAsync($"/bands/{bandId}")).ReadAsAsync<BandDetailResponse>();
        Assert.Equal(4096, band.Band.StorageUsedBytes);

        var download = await (await client.GetAsync($"/bands/{bandId}/song-files/{file.Id}/download")).ReadAsAsync<DownloadUrlResponse>();
        using var raw = new HttpClient();
        Assert.Equal(bytes, await raw.GetByteArrayAsync(download.Url));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/bands/{bandId}/song-files/{file.Id}")).StatusCode);
        Assert.Empty(await (await client.GetAsync($"/bands/{bandId}/songs/{song.Id}/files")).ReadAsAsync<List<SongFileResponse>>());
        Assert.Equal(0, (await (await client.GetAsync($"/bands/{bandId}")).ReadAsAsync<BandDetailResponse>()).Band.StorageUsedBytes);
    }

    [Fact]
    public async Task StandingPracticeDay_ProposesTheNextTwoWeeks_WithoutDoubleBooking()
    {
        var (client, bandId) = await BandAsync();
        var updated = await (await client.PatchAsJsonAsync($"/bands/{bandId}",
                new UpdateBandRequest(null, null, null, null, null, RecurringPracticeDay: IsoDayOfWeek.Thursday), TestJson.Options))
            .ReadAsAsync<BandResponse>();
        Assert.Equal(IsoDayOfWeek.Thursday, updated.RecurringPracticeDay);

        int created;
        using (var scope = factory.Services.CreateScope())
        {
            created = await scope.ServiceProvider.GetRequiredService<RecurringPracticeJob>()
                .RunAtAsync(SystemClock.Instance.GetCurrentInstant(), default);
        }

        Assert.Equal(2, created);
        var events = await (await client.GetAsync($"/bands/{bandId}/events")).ReadAsAsync<List<EventSummaryResponse>>();
        var practices = events.Where(e => e.Type == EventType.Practice).ToList();
        Assert.Equal(2, practices.Count);
        Assert.All(practices, e => Assert.Equal(EventStatus.Proposed, e.Status));
        Assert.All(practices, e => Assert.Equal(IsoDayOfWeek.Thursday, e.Date.DayOfWeek));

        // Cancel one; the next run must not resurrect it.
        await client.PostAsync($"/bands/{bandId}/events/{practices[0].Id}/cancel", null);
        using (var scope = factory.Services.CreateScope())
        {
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<RecurringPracticeJob>()
                .RunAtAsync(SystemClock.Instance.GetCurrentInstant(), default));
        }

        // Clearing the day stops the job for this band.
        await client.PatchAsJsonAsync($"/bands/{bandId}",
            new UpdateBandRequest(null, null, null, null, null, ClearRecurringPracticeDay: true), TestJson.Options);
        Assert.Null((await (await client.GetAsync($"/bands/{bandId}")).ReadAsAsync<BandDetailResponse>()).Band.RecurringPracticeDay);
    }
}
