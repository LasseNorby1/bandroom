using System.Text.Json;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Features.Events;
using Bandroom.Api.Infrastructure;
using Bandroom.Api.Infrastructure.Storage;
using Hangfire;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Demos;

public sealed record WorkerOptions
{
    public string? Url { get; init; }
}

public sealed record RequestPolishRequest(
    Guid? ReferenceTrackId = null,
    Guid? ReferenceVersionId = null,
    Guid? MixReferenceTrackId = null,
    Guid? MixReferenceVersionId = null);

public interface IPolishQueue
{
    void Enqueue(Guid polishJobId);
}

public sealed class HangfirePolishQueue : IPolishQueue
{
    public void Enqueue(Guid polishJobId) =>
        BackgroundJob.Enqueue<PolishJobRunner>(runner => runner.RunAsync(polishJobId, CancellationToken.None));
}

/// <summary>Test hosts run without hangfire; jobs simply stay queued.</summary>
public sealed class NoopPolishQueue(ILogger<NoopPolishQueue> logger) : IPolishQueue
{
    public void Enqueue(Guid polishJobId) =>
        logger.LogWarning("jobs disabled — polish job {JobId} stays queued", polishJobId);
}

public static class PolishEndpoints
{
    public static IEndpointRouteBuilder MapPolishEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGroup("/bands/{bandId:guid}")
            .RequireAuthorization()
            .WithTags("demos")
            .AddEndpointFilter<BandMemberFilter>()
            .MapPost("/song-ideas/{ideaId:guid}/polish", RequestPolishAsync)
            .WithSummary("Mix & master the idea's stems into a new demo version (ai)");

        return routes;
    }

    private static async Task<Results<Ok<PolishJobResponse>, NotFound, ValidationProblem>> RequestPolishAsync(
        Guid ideaId,
        RequestPolishRequest? request,
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        EntitlementOptions entitlements,
        WorkerOptions worker,
        IPolishQueue queue,
        CancellationToken ct)
    {
        var idea = await db.SongIdeas.SingleOrDefaultAsync(i => i.Id == ideaId, ct);
        if (idea is null)
        {
            return TypedResults.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(worker.Url))
        {
            errors["worker"] = ["Ai polish isn't available on this server (no audio worker configured)."];
        }

        await ValidateReferencePairAsync(
            db, errors, "reference", request?.ReferenceTrackId, request?.ReferenceVersionId, ct);
        await ValidateReferencePairAsync(
            db, errors, "mixReference", request?.MixReferenceTrackId, request?.MixReferenceVersionId, ct);

        var now = clock.GetCurrentInstant();
        var band = await db.Bands.SingleAsync(b => b.Id == bandContext.BandId, ct);
        if (!PlanLimits.IsPro(band, now, entitlements))
        {
            errors["plan"] = ["Ai polish is a pro feature."];
        }

        var stemCount = await db.Stems.CountAsync(
            s => s.SongIdeaId == ideaId && s.Status == VersionStatus.Ready, ct);
        if (stemCount == 0)
        {
            errors["stems"] = ["Upload at least one stem first."];
        }

        var running = await db.PolishJobs.AnyAsync(
            j => j.SongIdeaId == ideaId &&
                 (j.Status == PolishStatus.Queued || j.Status == PolishStatus.Processing), ct);
        if (running)
        {
            errors["job"] = ["A polish run is already in progress for this idea."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var job = new PolishJob
        {
            BandId = idea.BandId,
            SongIdeaId = ideaId,
            ReferenceTrackId = request?.ReferenceTrackId,
            ReferenceVersionId = request?.ReferenceVersionId,
            MixReferenceTrackId = request?.MixReferenceTrackId,
            MixReferenceVersionId = request?.MixReferenceVersionId,
            RequestedByMembershipId = bandContext.MembershipId!.Value,
            CreatedAt = now,
        };
        db.PolishJobs.Add(job);
        await db.SaveChangesAsync(ct);
        queue.Enqueue(job.Id);

        return TypedResults.Ok(new PolishJobResponse(job.Id, job.Status, null, null, job.CreatedAt, null));
    }

    /// <summary>Both stages take the same shape of reference: a library track or any ready version.</summary>
    private static async Task ValidateReferencePairAsync(
        AppDbContext db,
        Dictionary<string, string[]> errors,
        string field,
        Guid? trackId,
        Guid? versionId,
        CancellationToken ct)
    {
        if (trackId is not null && versionId is not null)
        {
            errors[field] = ["Pick either a library reference or a demo version, not both."];
            return;
        }

        if (trackId is { } referenceTrackId &&
            !await db.ReferenceTracks.AnyAsync(
                r => r.Id == referenceTrackId && r.Status == VersionStatus.Ready, ct))
        {
            errors[field] = ["Unknown reference track."];
        }

        if (versionId is { } referenceVersionId &&
            !await db.DemoVersions.AnyAsync(
                v => v.Id == referenceVersionId && v.Status == VersionStatus.Ready, ct))
        {
            errors[field] = ["Unknown demo version."];
        }
    }
}

/// <summary>
/// Runs on hangfire, outside any band context — every query opts out of the
/// filters explicitly. The worker downloads stems and uploads the result via
/// presigned urls, so audio never rides through the api.
/// </summary>
public sealed class PolishJobRunner(
    AppDbContext db,
    IFileStorage storage,
    IHttpClientFactory httpClientFactory,
    WorkerOptions worker,
    BandNotifier notifier,
    IClock clock,
    ILogger<PolishJobRunner> logger)
{
    private static readonly JsonSerializerOptions WorkerJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private sealed record WorkerStem(string Url, string Label);

    private sealed record WorkerRequest(
        IReadOnlyList<WorkerStem> Stems,
        string OutputUrl,
        bool Master,
        string? ReferenceUrl,
        string? MixReferenceUrl);

    private sealed record WorkerResponse(double DurationSeconds, float[]? Peaks = null);

    public async Task RunAsync(Guid polishJobId, CancellationToken ct)
    {
        var job = await db.PolishJobs.IgnoreQueryFilters().SingleOrDefaultAsync(j => j.Id == polishJobId, ct);
        if (job is null || job.Status != PolishStatus.Queued)
        {
            return;
        }

        job.Status = PolishStatus.Processing;
        await db.SaveChangesAsync(ct);
        await notifier.DemoChangedAsync(job.BandId, job.SongIdeaId, "polish", ct);

        try
        {
            var stems = await db.Stems.IgnoreQueryFilters()
                .Where(s => s.SongIdeaId == job.SongIdeaId && s.Status == VersionStatus.Ready)
                .ToListAsync(ct);
            if (stems.Count == 0)
            {
                throw new InvalidOperationException("No ready stems.");
            }

            var outputVersionId = Guid.CreateVersion7();
            var outputKey = $"bands/{job.BandId}/ideas/{job.SongIdeaId}/versions/{outputVersionId}.wav";
            var outputUpload = await storage.CreateUploadAsync(
                outputKey, "audio/wav", ct, StorageAudience.Worker);

            var referenceKey = await ResolveReferenceKeyAsync(job.ReferenceTrackId, job.ReferenceVersionId, ct);
            var mixReferenceKey = await ResolveReferenceKeyAsync(
                job.MixReferenceTrackId, job.MixReferenceVersionId, ct);

            string? Presign(string? key) => key is null
                ? null
                : storage.GetDownloadUrl(key, TimeSpan.FromHours(1), StorageAudience.Worker);

            var referenceUrl = Presign(referenceKey);
            // Same track for both stages → one url, so the worker downloads it once.
            var mixReferenceUrl = mixReferenceKey == referenceKey ? referenceUrl : Presign(mixReferenceKey);

            var request = new WorkerRequest(
                stems.Select(s => new WorkerStem(
                    storage.GetDownloadUrl(s.FileKey, TimeSpan.FromHours(1), StorageAudience.Worker),
                    s.Name ?? s.Label.ToString().ToLowerInvariant())).ToList(),
                outputUpload.Url,
                Master: true,
                ReferenceUrl: referenceUrl,
                MixReferenceUrl: mixReferenceUrl);

            var http = httpClientFactory.CreateClient("worker");
            var response = await http.PostAsJsonAsync($"{worker.Url!.TrimEnd('/')}/polish", request, WorkerJson, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<WorkerResponse>(WorkerJson, ct)
                         ?? throw new InvalidOperationException("Empty worker response.");

            var size = await storage.GetSizeAsync(outputKey, ct)
                       ?? throw new InvalidOperationException("Worker reported success but no output object exists.");

            var number = await db.DemoVersions.IgnoreQueryFilters()
                .Where(v => v.SongIdeaId == job.SongIdeaId)
                .Select(v => (int?)v.Number)
                .MaxAsync(ct) ?? 0;
            db.DemoVersions.Add(new DemoVersion
            {
                Id = outputVersionId,
                BandId = job.BandId,
                SongIdeaId = job.SongIdeaId,
                Number = number + 1,
                FileKey = outputKey,
                FileName = "ai-demo-mix.wav",
                ContentType = "audio/wav",
                SizeBytes = size,
                DurationSeconds = result.DurationSeconds,
                Peaks = WaveformPeaks.Normalize(result.Peaks),
                Status = VersionStatus.Ready,
                Kind = VersionKind.AiMix,
                UploadedByMembershipId = job.RequestedByMembershipId,
                CreatedAt = clock.GetCurrentInstant(),
            });

            var band = await db.Bands.SingleAsync(b => b.Id == job.BandId, ct);
            band.StorageUsedBytes += size;

            job.Status = PolishStatus.Done;
            job.OutputVersionId = outputVersionId;
            job.CompletedAt = clock.GetCurrentInstant();
            await db.SaveChangesAsync(ct);
            await notifier.DemoChangedAsync(job.BandId, job.SongIdeaId, "polishDone", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "polish job {JobId} failed", polishJobId);
            job.Status = PolishStatus.Failed;
            job.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            job.CompletedAt = clock.GetCurrentInstant();
            await db.SaveChangesAsync(CancellationToken.None);
            await notifier.DemoChangedAsync(job.BandId, job.SongIdeaId, "polishFailed", CancellationToken.None);
        }
    }

    private async Task<string?> ResolveReferenceKeyAsync(Guid? trackId, Guid? versionId, CancellationToken ct)
    {
        if (trackId is { } referenceTrackId)
        {
            return await db.ReferenceTracks.IgnoreQueryFilters()
                .Where(r => r.Id == referenceTrackId)
                .Select(r => r.FileKey)
                .SingleOrDefaultAsync(ct);
        }

        if (versionId is { } referenceVersionId)
        {
            return await db.DemoVersions.IgnoreQueryFilters()
                .Where(v => v.Id == referenceVersionId)
                .Select(v => v.FileKey)
                .SingleOrDefaultAsync(ct);
        }

        return null;
    }
}
