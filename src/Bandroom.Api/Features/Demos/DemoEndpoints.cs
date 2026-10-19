using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Features.Events;
using Bandroom.Api.Infrastructure;
using Bandroom.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Demos;

public static class DemoEndpoints
{
    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder routes)
    {
        var band = routes.MapGroup("/bands/{bandId:guid}")
            .RequireAuthorization()
            .WithTags("demos")
            .AddEndpointFilter<BandMemberFilter>();

        band.MapPost("/songs/{songId:guid}/versions/uploads", InitVersionUploadAsync)
            .WithSummary("Start a demo upload (presigned url)");
        band.MapPost("/versions/{versionId:guid}/confirm", ConfirmVersionAsync)
            .WithSummary("Finish an upload — verifies the object and its size");
        band.MapGet("/versions/{versionId:guid}/stream", StreamAsync).WithSummary("Presigned playback url");
        band.MapDelete("/versions/{versionId:guid}", DeleteVersionAsync).WithSummary("Delete a version (uploader/admin)");

        band.MapPost("/songs/{songId:guid}/stems/uploads", InitStemUploadAsync)
            .WithSummary("Start a stem upload");
        band.MapPost("/stems/{stemId:guid}/confirm", ConfirmStemAsync).WithSummary("Finish a stem upload");
        band.MapDelete("/stems/{stemId:guid}", DeleteStemAsync).WithSummary("Delete a stem (uploader/admin)");

        return routes;
    }

    /// <summary>Versions, stems and polish jobs of a song — SongEndpoints folds this into the song detail.</summary>
    public static async Task<SongTakesResponse> LoadTakesAsync(AppDbContext db, Guid songId, Guid me, CancellationToken ct)
    {
        var memberNames = await db.Memberships
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.Id, u.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        var versionRows = await db.DemoVersions
            .Where(v => v.SongId == songId && v.Status == VersionStatus.Ready)
            .OrderByDescending(v => v.Number)
            .ToListAsync(ct);
        var versionIds = versionRows.Select(v => v.Id).ToList();
        var fires = await db.Reactions
            .Where(r => versionIds.Contains(r.VersionId))
            .GroupBy(r => r.VersionId)
            .Select(g => new { VersionId = g.Key, Count = g.Count(), Mine = g.Any(r => r.MembershipId == me) })
            .ToDictionaryAsync(x => x.VersionId, ct);
        var analyses = await db.VersionAnalyses
            .Where(a => versionIds.Contains(a.VersionId) && a.Status == AnalysisStatus.Done)
            .ToDictionaryAsync(a => a.VersionId, ct);
        var versions = versionRows
            .Select(v => new VersionResponse(
                v.Id, v.Number, v.Kind, v.FileName, v.ContentType, v.SizeBytes, v.DurationSeconds, v.Peaks,
                memberNames.GetValueOrDefault(v.UploadedByMembershipId, "?"), v.CreatedAt,
                fires.GetValueOrDefault(v.Id)?.Count ?? 0, fires.GetValueOrDefault(v.Id)?.Mine ?? false,
                analyses.GetValueOrDefault(v.Id)?.Bpm, analyses.GetValueOrDefault(v.Id)?.Key))
            .ToList();

        var stems = await db.Stems
            .Where(s => s.SongId == songId && s.Status != VersionStatus.Failed)
            .OrderBy(s => s.Label)
            .ThenBy(s => s.CreatedAt)
            .Select(s => new StemResponse(s.Id, s.Label, s.Name, s.FileName, s.SizeBytes, s.Status, s.CreatedAt))
            .ToListAsync(ct);

        var jobs = await db.PolishJobs
            .Where(j => j.SongId == songId)
            .OrderByDescending(j => j.CreatedAt)
            .Take(5)
            .Select(j => new PolishJobResponse(j.Id, j.Status, j.OutputVersionId, j.Error, j.CreatedAt, j.CompletedAt))
            .ToListAsync(ct);

        return new SongTakesResponse(versions, stems, jobs);
    }

    private static async Task<Results<Ok<InitUploadResponse>, NotFound, ValidationProblem>> InitVersionUploadAsync(
        Guid songId,
        InitUploadRequest request,
        BandContext bandContext,
        AppDbContext db,
        IFileStorage storage,
        IClock clock,
        EntitlementOptions entitlements,
        CancellationToken ct)
    {
        var song = await db.Songs.SingleOrDefaultAsync(i => i.Id == songId, ct);
        if (song is null)
        {
            return TypedResults.NotFound();
        }

        var now = clock.GetCurrentInstant();
        var band = bandContext.Band;
        if (UploadRules.Validate(request.FileName, request.ContentType, request.SizeBytes, band, now, entitlements) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        for (var attempt = 0; ; attempt++)
        {
            var number = await db.DemoVersions.Where(v => v.SongId == songId).Select(v => (int?)v.Number).MaxAsync(ct) ?? 0;
            var version = new DemoVersion
            {
                BandId = song.BandId,
                SongId = songId,
                Number = number + 1,
                FileKey = $"bands/{song.BandId}/songs/{songId}/versions/{Guid.CreateVersion7()}{UploadRules.Extension(request.FileName)}",
                FileName = request.FileName.Trim(),
                ContentType = request.ContentType,
                UploadedByMembershipId = bandContext.MembershipId!.Value,
                CreatedAt = now,
            };
            db.DemoVersions.Add(version);
            try
            {
                await db.SaveChangesAsync(ct);
                var upload = await storage.CreateUploadAsync(version.FileKey, version.ContentType, ct);
                return TypedResults.Ok(new InitUploadResponse(version.Id, upload.Url, upload.ExpiresAt));
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                // Lost the (SongId, Number) race — recompute once.
                db.Entry(version).State = EntityState.Detached;
            }
        }
    }

    private static async Task<Results<Ok<VersionResponse>, NotFound, ValidationProblem>> ConfirmVersionAsync(
        Guid versionId,
        ConfirmUploadRequest request,
        BandContext bandContext,
        AppDbContext db,
        IFileStorage storage,
        BandNotifier notifier,
        IAnalysisQueue analysisQueue,
        IClock clock,
        CancellationToken ct)
    {
        var version = await db.DemoVersions.SingleOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null)
        {
            return TypedResults.NotFound();
        }

        if (version.Status == VersionStatus.Uploading)
        {
            var size = await storage.GetSizeAsync(version.FileKey, ct);
            if (size is null or 0)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["upload"] = ["The file hasn't arrived in storage — upload it first, then confirm."],
                });
            }

            version.SizeBytes = size.Value;
            version.DurationSeconds = request.DurationSeconds;
            version.Peaks = WaveformPeaks.Normalize(request.Peaks);
            version.Status = VersionStatus.Ready;

            bandContext.Band.StorageUsedBytes += size.Value;
            await db.SaveChangesAsync(ct);
            await notifier.DemoChangedAsync(version.BandId, version.SongId, "version", ct);

            // Bpm + key arrive later, from the worker — never in this request.
            await AnalysisJobRunner.QueueAsync(db, version.BandId, version.Id, clock.GetCurrentInstant(), ct);
            analysisQueue.Enqueue(version.Id);
        }

        var uploaderName = await db.Memberships
            .Where(m => m.Id == version.UploadedByMembershipId)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => u.DisplayName)
            .SingleOrDefaultAsync(ct) ?? "?";
        return TypedResults.Ok(new VersionResponse(
            version.Id, version.Number, version.Kind, version.FileName, version.ContentType,
            version.SizeBytes, version.DurationSeconds, version.Peaks, uploaderName, version.CreatedAt,
            0, false, null, null));
    }

    private static async Task<Results<Ok<StreamUrlResponse>, NotFound>> StreamAsync(
        Guid versionId,
        AppDbContext db,
        IFileStorage storage,
        CancellationToken ct)
    {
        var version = await db.DemoVersions
            .SingleOrDefaultAsync(v => v.Id == versionId && v.Status == VersionStatus.Ready, ct);
        if (version is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new StreamUrlResponse(storage.GetDownloadUrl(version.FileKey, TimeSpan.FromHours(1))));
    }

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult>> DeleteVersionAsync(
        Guid versionId,
        BandContext bandContext,
        AppDbContext db,
        IFileStorage storage,
        BandNotifier notifier,
        CancellationToken ct)
    {
        var version = await db.DemoVersions.SingleOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null)
        {
            return TypedResults.NotFound();
        }

        if (bandContext.Role != BandRole.Admin && version.UploadedByMembershipId != bandContext.MembershipId)
        {
            return TypedResults.Forbid();
        }

        await db.Comments
            .Where(c => c.TargetType == CommentTarget.DemoVersion && c.TargetId == versionId)
            .ExecuteDeleteAsync(ct);

        var band = bandContext.Band;
        band.StorageUsedBytes = Math.Max(0, band.StorageUsedBytes - version.SizeBytes);
        db.DemoVersions.Remove(version);
        await db.SaveChangesAsync(ct);

        try
        {
            await storage.DeleteAsync(version.FileKey, ct);
        }
        catch
        {
            // Orphaned objects are cheaper than failed deletes; a sweep job can
            // reconcile storage later.
        }

        await notifier.DemoChangedAsync(version.BandId, version.SongId, "versionDeleted", ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<InitStemUploadResponse>, NotFound, ValidationProblem>> InitStemUploadAsync(
        Guid songId,
        InitStemUploadRequest request,
        BandContext bandContext,
        AppDbContext db,
        IFileStorage storage,
        IClock clock,
        EntitlementOptions entitlements,
        CancellationToken ct)
    {
        var song = await db.Songs.SingleOrDefaultAsync(i => i.Id == songId, ct);
        if (song is null)
        {
            return TypedResults.NotFound();
        }

        var now = clock.GetCurrentInstant();
        var band = bandContext.Band;
        if (UploadRules.Validate(request.FileName, request.ContentType, request.SizeBytes, band, now, entitlements) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var stem = new Stem
        {
            BandId = song.BandId,
            SongId = songId,
            Label = request.Label,
            Name = request.Name?.Trim(),
            FileKey = $"bands/{song.BandId}/songs/{songId}/stems/{Guid.CreateVersion7()}{UploadRules.Extension(request.FileName)}",
            FileName = request.FileName.Trim(),
            ContentType = request.ContentType,
            UploadedByMembershipId = bandContext.MembershipId!.Value,
            CreatedAt = now,
        };
        db.Stems.Add(stem);
        await db.SaveChangesAsync(ct);

        var upload = await storage.CreateUploadAsync(stem.FileKey, stem.ContentType, ct);
        return TypedResults.Ok(new InitStemUploadResponse(stem.Id, upload.Url, upload.ExpiresAt));
    }

    private static async Task<Results<Ok<StemResponse>, NotFound, ValidationProblem>> ConfirmStemAsync(
        Guid stemId,
        BandContext bandContext,
        AppDbContext db,
        IFileStorage storage,
        BandNotifier notifier,
        CancellationToken ct)
    {
        var stem = await db.Stems.SingleOrDefaultAsync(s => s.Id == stemId, ct);
        if (stem is null)
        {
            return TypedResults.NotFound();
        }

        if (stem.Status == VersionStatus.Uploading)
        {
            var size = await storage.GetSizeAsync(stem.FileKey, ct);
            if (size is null or 0)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["upload"] = ["The file hasn't arrived in storage — upload it first, then confirm."],
                });
            }

            stem.SizeBytes = size.Value;
            stem.Status = VersionStatus.Ready;
            bandContext.Band.StorageUsedBytes += size.Value;
            await db.SaveChangesAsync(ct);
            await notifier.DemoChangedAsync(stem.BandId, stem.SongId, "stem", ct);
        }

        return TypedResults.Ok(new StemResponse(
            stem.Id, stem.Label, stem.Name, stem.FileName, stem.SizeBytes, stem.Status, stem.CreatedAt));
    }

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult>> DeleteStemAsync(
        Guid stemId,
        BandContext bandContext,
        AppDbContext db,
        IFileStorage storage,
        BandNotifier notifier,
        CancellationToken ct)
    {
        var stem = await db.Stems.SingleOrDefaultAsync(s => s.Id == stemId, ct);
        if (stem is null)
        {
            return TypedResults.NotFound();
        }

        if (bandContext.Role != BandRole.Admin && stem.UploadedByMembershipId != bandContext.MembershipId)
        {
            return TypedResults.Forbid();
        }

        var band = bandContext.Band;
        band.StorageUsedBytes = Math.Max(0, band.StorageUsedBytes - stem.SizeBytes);
        db.Stems.Remove(stem);
        await db.SaveChangesAsync(ct);

        try
        {
            await storage.DeleteAsync(stem.FileKey, ct);
        }
        catch
        {
            // see DeleteVersionAsync
        }

        await notifier.DemoChangedAsync(stem.BandId, stem.SongId, "stemDeleted", ct);
        return TypedResults.NoContent();
    }
}
