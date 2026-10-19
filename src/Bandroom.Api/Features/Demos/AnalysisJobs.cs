using System.Text.Json;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Events;
using Bandroom.Api.Infrastructure.Storage;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Demos;

public interface IAnalysisQueue
{
    void Enqueue(Guid versionId);
}

public sealed class HangfireAnalysisQueue : IAnalysisQueue
{
    public void Enqueue(Guid versionId) =>
        BackgroundJob.Enqueue<AnalysisJobRunner>(runner => runner.RunAsync(versionId, CancellationToken.None));
}

/// <summary>Test hosts run without hangfire; analyses simply stay queued.</summary>
public sealed class NoopAnalysisQueue(ILogger<NoopAnalysisQueue> logger) : IAnalysisQueue
{
    public void Enqueue(Guid versionId) =>
        logger.LogWarning("jobs disabled — analysis for version {VersionId} stays queued", versionId);
}

/// <summary>
/// Bpm + key for a demo version, from the mir worker (spec §5.5 "demo
/// auto-metadata": not an llm). Runs after confirm, off the request path, and
/// writes only to the derived VersionAnalysis row keyed by version + model.
/// </summary>
public sealed class AnalysisJobRunner(
    AppDbContext db,
    IFileStorage storage,
    IHttpClientFactory httpClientFactory,
    WorkerOptions worker,
    BandNotifier notifier,
    IClock clock,
    ILogger<AnalysisJobRunner> logger)
{
    public const string ModelVersion = "librosa-tempo-ks-1";

    private static readonly JsonSerializerOptions WorkerJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private sealed record AnalyzeRequest(string Url);

    private sealed record AnalyzeResponse(double? Bpm, string? Key, double? KeyConfidence);

    /// <summary>Queue (or re-queue) the derived row; idempotent per version.</summary>
    public static async Task QueueAsync(AppDbContext db, Guid bandId, Guid versionId, Instant now, CancellationToken ct)
    {
        var existing = await db.VersionAnalyses.IgnoreQueryFilters().SingleOrDefaultAsync(a => a.VersionId == versionId, ct);
        if (existing is null)
        {
            db.VersionAnalyses.Add(new VersionAnalysis
            {
                VersionId = versionId,
                BandId = bandId,
                ModelVersion = ModelVersion,
                CreatedAt = now,
            });
        }
        else
        {
            existing.Status = AnalysisStatus.Queued;
            existing.Error = null;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task RunAsync(Guid versionId, CancellationToken ct)
    {
        var analysis = await db.VersionAnalyses.IgnoreQueryFilters().SingleOrDefaultAsync(a => a.VersionId == versionId, ct);
        var version = await db.DemoVersions.IgnoreQueryFilters().SingleOrDefaultAsync(v => v.Id == versionId, ct);
        if (analysis is null || version is null || analysis.Status != AnalysisStatus.Queued)
        {
            return;
        }

        if (string.IsNullOrEmpty(worker.Url))
        {
            analysis.Status = AnalysisStatus.Failed;
            analysis.Error = "No audio worker configured.";
            await db.SaveChangesAsync(ct);
            return;
        }

        try
        {
            var url = storage.GetDownloadUrl(version.FileKey, TimeSpan.FromHours(1), StorageAudience.Worker);
            var http = httpClientFactory.CreateClient("worker");
            var response = await http.PostAsJsonAsync($"{worker.Url.TrimEnd('/')}/analyze", new AnalyzeRequest(url), WorkerJson, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<AnalyzeResponse>(WorkerJson, ct)
                         ?? throw new InvalidOperationException("Empty worker response.");

            analysis.Bpm = result.Bpm;
            analysis.Key = result.Key;
            analysis.KeyConfidence = result.KeyConfidence;
            analysis.Status = AnalysisStatus.Done;
            analysis.CompletedAt = clock.GetCurrentInstant();
            await db.SaveChangesAsync(ct);
            await notifier.DemoChangedAsync(version.BandId, version.SongId, "analysis", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "analysis for version {VersionId} failed", versionId);
            analysis.Status = AnalysisStatus.Failed;
            analysis.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            analysis.CompletedAt = clock.GetCurrentInstant();
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }
}
