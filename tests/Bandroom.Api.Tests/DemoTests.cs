using System.Net;
using System.Net.Http.Json;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Features.Comments;
using Bandroom.Api.Features.Demos;
using Bandroom.Api.Tests.Support;
using Xunit;

namespace Bandroom.Api.Tests;

[Collection("api")]
public class DemoTests(ApiFactory factory)
{
    private async Task<(HttpClient Client, Guid BandId)> BandAsync()
    {
        var client = await factory.RegisterUserAsync("Lasse");
        var created = await (await client.PostAsJsonAsync(
                "/bands", new CreateBandRequest($"Band-{Guid.NewGuid():N}", null), TestJson.Options))
            .ReadAsAsync<BandDetailResponse>();
        return (client, created.Band.Id);
    }

    private static async Task<Guid> CreateIdeaAsync(HttpClient client, Guid bandId, string title = "Bridge idea")
    {
        var response = await client.PostAsJsonAsync(
            $"/bands/{bandId}/song-ideas", new CreateIdeaRequest(title), TestJson.Options);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadAsAsync<IdeaSummaryResponse>()).Id;
    }

    private static async Task<VersionResponse> UploadVersionAsync(
        HttpClient client, Guid bandId, Guid ideaId, byte[] bytes, string fileName = "take.mp3")
    {
        var init = await (await client.PostAsJsonAsync(
                $"/bands/{bandId}/song-ideas/{ideaId}/versions/uploads",
                new InitUploadRequest(fileName, "audio/mpeg", bytes.Length), TestJson.Options))
            .ReadAsAsync<InitUploadResponse>();

        using var raw = new HttpClient();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
        var put = await raw.PutAsync(init.UploadUrl, content);
        Assert.True(put.IsSuccessStatusCode, $"presigned put failed: {put.StatusCode}");

        return await (await client.PostAsJsonAsync(
                $"/bands/{bandId}/versions/{init.VersionId}/confirm",
                new ConfirmUploadRequest(12.5), TestJson.Options))
            .ReadAsAsync<VersionResponse>();
    }

    [Fact]
    public async Task UploadFlow_PresignPutConfirm_MakesAPlayableVersion()
    {
        var (client, bandId) = await BandAsync();
        var ideaId = await CreateIdeaAsync(client, bandId);
        var bytes = new byte[64 * 1024];
        Random.Shared.NextBytes(bytes);

        var version = await UploadVersionAsync(client, bandId, ideaId, bytes);
        Assert.Equal(1, version.Number);
        Assert.Equal(bytes.Length, version.SizeBytes);
        Assert.Equal(12.5, version.DurationSeconds);

        // The stream url serves the exact bytes back.
        var stream = await (await client.GetAsync($"/bands/{bandId}/versions/{version.Id}/stream"))
            .ReadAsAsync<StreamUrlResponse>();
        using var raw = new HttpClient();
        var downloaded = await raw.GetByteArrayAsync(stream.Url);
        Assert.Equal(bytes, downloaded);

        // Storage accounting reflects the verified size.
        var band = await (await client.GetAsync($"/bands/{bandId}")).ReadAsAsync<BandDetailResponse>();
        Assert.Equal(bytes.Length, band.Band.StorageUsedBytes);

        // A second upload becomes v2 and the idea summary counts both.
        await UploadVersionAsync(client, bandId, ideaId, bytes, "take2.mp3");
        var ideas = await (await client.GetAsync($"/bands/{bandId}/song-ideas"))
            .ReadAsAsync<List<IdeaSummaryResponse>>();
        Assert.Equal(2, Assert.Single(ideas).VersionCount);
    }

    [Fact]
    public async Task Confirm_WithoutUploadedObject_Fails()
    {
        var (client, bandId) = await BandAsync();
        var ideaId = await CreateIdeaAsync(client, bandId);
        var init = await (await client.PostAsJsonAsync(
                $"/bands/{bandId}/song-ideas/{ideaId}/versions/uploads",
                new InitUploadRequest("ghost.mp3", "audio/mpeg", 1000), TestJson.Options))
            .ReadAsAsync<InitUploadResponse>();

        var confirm = await client.PostAsJsonAsync(
            $"/bands/{bandId}/versions/{init.VersionId}/confirm", new ConfirmUploadRequest(null), TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
    }

    [Fact]
    public async Task Upload_OverQuota_IsRejected()
    {
        var (client, bandId) = await BandAsync();
        var ideaId = await CreateIdeaAsync(client, bandId);

        var response = await client.PostAsJsonAsync(
            $"/bands/{bandId}/song-ideas/{ideaId}/versions/uploads",
            new InitUploadRequest("huge.wav", "audio/wav", 500L * 1024 * 1024), TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteVersion_ReleasesStorageAndComments()
    {
        var (client, bandId) = await BandAsync();
        var ideaId = await CreateIdeaAsync(client, bandId);
        var bytes = new byte[32 * 1024];
        var version = await UploadVersionAsync(client, bandId, ideaId, bytes);

        await client.PostAsJsonAsync(
            $"/bands/{bandId}/comments",
            new CreateCommentRequest(CommentTarget.DemoVersion, version.Id, "chorus hits at", 83), TestJson.Options);

        var delete = await client.DeleteAsync($"/bands/{bandId}/versions/{version.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var band = await (await client.GetAsync($"/bands/{bandId}")).ReadAsAsync<BandDetailResponse>();
        Assert.Equal(0, band.Band.StorageUsedBytes);

        var comments = await (await client.GetAsync(
                $"/bands/{bandId}/comments?targetType=demoVersion&targetId={version.Id}"))
            .ReadAsAsync<List<CommentResponse>>();
        Assert.Empty(comments);
    }

    [Fact]
    public async Task Stems_UploadAndPolish_QueuesAJob()
    {
        var (client, bandId) = await BandAsync();
        var ideaId = await CreateIdeaAsync(client, bandId);

        var init = await (await client.PostAsJsonAsync(
                $"/bands/{bandId}/song-ideas/{ideaId}/stems/uploads",
                new InitStemUploadRequest(StemLabel.Drums, null, "drums.wav", "audio/wav", 2048), TestJson.Options))
            .ReadAsAsync<InitStemUploadResponse>();
        using var raw = new HttpClient();
        var bytes = new byte[2048];
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        (await raw.PutAsync(init.UploadUrl, content)).EnsureSuccessStatusCode();
        var stem = await (await client.PostAsJsonAsync(
                $"/bands/{bandId}/stems/{init.StemId}/confirm", new { }, TestJson.Options))
            .ReadAsAsync<StemResponse>();
        Assert.Equal(VersionStatus.Ready, stem.Status);

        var polish = await (await client.PostAsJsonAsync(
                $"/bands/{bandId}/song-ideas/{ideaId}/polish", new { }, TestJson.Options))
            .ReadAsAsync<PolishJobResponse>();
        Assert.Equal(PolishStatus.Queued, polish.Status);

        // A second request while one is pending is refused.
        var second = await client.PostAsJsonAsync(
            $"/bands/{bandId}/song-ideas/{ideaId}/polish", new { }, TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);

        var detail = await (await client.GetAsync($"/bands/{bandId}/song-ideas/{ideaId}"))
            .ReadAsAsync<IdeaDetailResponse>();
        Assert.Single(detail.Stems);
        Assert.Single(detail.PolishJobs);
    }

    [Fact]
    public async Task References_UploadAndPolishAgainstThem()
    {
        var (client, bandId) = await BandAsync();
        var ideaId = await CreateIdeaAsync(client, bandId);

        // One ready stem so polish is possible.
        var stemInit = await (await client.PostAsJsonAsync(
                $"/bands/{bandId}/song-ideas/{ideaId}/stems/uploads",
                new InitStemUploadRequest(StemLabel.Guitar, null, "gtr.wav", "audio/wav", 4096), TestJson.Options))
            .ReadAsAsync<InitStemUploadResponse>();
        using var raw = new HttpClient();
        var stemContent = new ByteArrayContent(new byte[4096]);
        stemContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        (await raw.PutAsync(stemInit.UploadUrl, stemContent)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/bands/{bandId}/stems/{stemInit.StemId}/confirm", null)).EnsureSuccessStatusCode();

        // Reference library upload.
        var refInit = await (await client.PostAsJsonAsync(
                $"/bands/{bandId}/references/uploads",
                new InitReferenceUploadRequest("Our sound", "ref.wav", "audio/wav", 8192), TestJson.Options))
            .ReadAsAsync<InitReferenceUploadResponse>();
        var refContent = new ByteArrayContent(new byte[8192]);
        refContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        (await raw.PutAsync(refInit.UploadUrl, refContent)).EnsureSuccessStatusCode();
        var reference = await (await client.PostAsync(
                $"/bands/{bandId}/references/{refInit.ReferenceId}/confirm", null))
            .ReadAsAsync<ReferenceResponse>();
        Assert.Equal(VersionStatus.Ready, reference.Status);

        var listed = await (await client.GetAsync($"/bands/{bandId}/references"))
            .ReadAsAsync<List<ReferenceResponse>>();
        Assert.Equal("Our sound", Assert.Single(listed).Title);

        // Polish referencing the library track queues; a bogus reference is rejected.
        var polish = await (await client.PostAsJsonAsync(
                $"/bands/{bandId}/song-ideas/{ideaId}/polish",
                new RequestPolishRequest(reference.Id, null), TestJson.Options))
            .ReadAsAsync<PolishJobResponse>();
        Assert.Equal(PolishStatus.Queued, polish.Status);

        var bogus = await client.PostAsJsonAsync(
            $"/bands/{bandId}/song-ideas/{ideaId}/polish",
            new RequestPolishRequest(Guid.NewGuid(), null), TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, bogus.StatusCode);
    }

    [Fact]
    public async Task Polish_WithoutStems_IsRejected()
    {
        var (client, bandId) = await BandAsync();
        var ideaId = await CreateIdeaAsync(client, bandId);
        var response = await client.PostAsJsonAsync(
            $"/bands/{bandId}/song-ideas/{ideaId}/polish", new { }, TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
