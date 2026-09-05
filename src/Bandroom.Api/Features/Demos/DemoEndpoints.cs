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

        band.MapGet("/song-ideas", ListIdeasAsync).WithSummary("Song ideas with version counts");
        band.MapPost("/song-ideas", CreateIdeaAsync).WithSummary("Start a song idea");
        band.MapGet("/song-ideas/{ideaId:guid}", IdeaDetailAsync).WithSummary("Idea with versions, stems and polish jobs");
        band.MapPatch("/song-ideas/{ideaId:guid}", UpdateIdeaAsync).WithSummary("Rename/relink/restatus an idea");

        band.MapPost("/song-ideas/{ideaId:guid}/versions/uploads", InitVersionUploadAsync)
            .WithSummary("Start a demo upload (presigned url)");
        band.MapPost("/versions/{versionId:guid}/confirm", ConfirmVersionAsync)
            .WithSummary("Finish an upload — verifies the object and its size");
        band.MapGet("/versions/{versionId:guid}/stream", StreamAsync).WithSummary("Presigned playback url");
        band.MapDelete("/versions/{versionId:guid}", DeleteVersionAsync).WithSummary("Delete a version (uploader/admin)");

        band.MapPost("/song-ideas/{ideaId:guid}/stems/uploads", InitStemUploadAsync)
            .WithSummary("Start a stem upload");
        band.MapPost("/stems/{stemId:guid}/confirm", ConfirmStemAsync).WithSummary("Finish a stem upload");
        band.MapDelete("/stems/{stemId:guid}", DeleteStemAsync).WithSummary("Delete a stem (uploader/admin)");

        return routes;
    }

    private static async Task<Ok<List<IdeaSummaryResponse>>> ListIdeasAsync(AppDbContext db, CancellationToken ct)
    {
        var ideas = await db.SongIdeas
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new IdeaSummaryResponse(
                i.Id, i.Title, i.Status, i.SongId,
                db.DemoVersions.Count(v => v.SongIdeaId == i.Id && v.Status == VersionStatus.Ready),
                db.DemoVersions
                    .Where(v => v.SongIdeaId == i.Id && v.Status == VersionStatus.Ready)
                    .Max(v => (Instant?)v.CreatedAt),
                i.CreatedAt))
            .ToListAsync(ct);
        return TypedResults.Ok(ideas);
    }

    private static async Task<Results<Created<IdeaSummaryResponse>, ValidationProblem>> CreateIdeaAsync(
        CreateIdeaRequest request,
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 120)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["title"] = ["Title is required (max 120 characters)."],
            });
        }

        var idea = new SongIdea
        {
            BandId = bandContext.BandId!.Value,
            Title = request.Title.Trim(),
            CreatedByMembershipId = bandContext.MembershipId!.Value,
            CreatedAt = clock.GetCurrentInstant(),
        };
        db.SongIdeas.Add(idea);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/bands/{idea.BandId}/song-ideas/{idea.Id}",
            new IdeaSummaryResponse(idea.Id, idea.Title, idea.Status, idea.SongId, 0, null, idea.CreatedAt));
    }

    private static async Task<Results<Ok<IdeaDetailResponse>, NotFound>> IdeaDetailAsync(
        Guid ideaId,
        AppDbContext db,
        CancellationToken ct)
    {
        var idea = await db.SongIdeas.SingleOrDefaultAsync(i => i.Id == ideaId, ct);
        if (idea is null)
        {
            return TypedResults.NotFound();
        }

        var memberNames = await db.Memberships
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.Id, u.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        var versions = (await db.DemoVersions
                .Where(v => v.SongIdeaId == ideaId && v.Status == VersionStatus.Ready)
                .OrderByDescending(v => v.Number)
                .ToListAsync(ct))
            .Select(v => new VersionResponse(
                v.Id, v.Number, v.Kind, v.FileName, v.ContentType, v.SizeBytes, v.DurationSeconds, v.Peaks,
                memberNames.GetValueOrDefault(v.UploadedByMembershipId, "?"), v.CreatedAt))
            .ToList();

        var stems = await db.Stems
            .Where(s => s.SongIdeaId == ideaId && s.Status != VersionStatus.Failed)
            .OrderBy(s => s.Label)
            .ThenBy(s => s.CreatedAt)
            .Select(s => new StemResponse(s.Id, s.Label, s.Name, s.FileName, s.SizeBytes, s.Status, s.CreatedAt))
            .ToListAsync(ct);

        var jobs = await db.PolishJobs
            .Where(j => j.SongIdeaId == ideaId)
            .OrderByDescending(j => j.CreatedAt)
            .Take(5)
            .Select(j => new PolishJobResponse(j.Id, j.Status, j.OutputVersionId, j.Error, j.CreatedAt, j.CompletedAt))
            .ToListAsync(ct);

        return TypedResults.Ok(new IdeaDetailResponse(
            idea.Id, idea.Title, idea.Status, idea.SongId, versions, stems, jobs));
    }

    private static async Task<Results<Ok<IdeaSummaryResponse>, NotFound, ValidationProblem>> UpdateIdeaAsync(
        Guid ideaId,
        UpdateIdeaRequest request,
        AppDbContext db,
        CancellationToken ct)
    {
        var idea = await db.SongIdeas.SingleOrDefaultAsync(i => i.Id == ideaId, ct);
        if (idea is null)
        {
            return TypedResults.NotFound();
        }

        if (request.Title is not null)
        {
            var title = request.Title.Trim();
            if (title.Length is 0 or > 120)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["title"] = ["Title must be 1–120 characters."],
                });
            }

            idea.Title = title;
        }

        if (request.Status is { } status)
        {
            idea.Status = status;
        }

        if (request.SongId is { } songId)
        {
            var exists = await db.Songs.AnyAsync(s => s.Id == songId, ct);
            if (!exists)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["songId"] = ["Unknown song."],
                });
            }

            idea.SongId = songId;
        }

        await db.SaveChangesAsync(ct);
        var versionCount = await db.DemoVersions
            .CountAsync(v => v.SongIdeaId == ideaId && v.Status == VersionStatus.Ready, ct);
        return TypedResults.Ok(new IdeaSummaryResponse(
            idea.Id, idea.Title, idea.Status, idea.SongId, versionCount, null, idea.CreatedAt));
    }

    private static async Task<Results<Ok<InitUploadResponse>, NotFound, ValidationProblem>> InitVersionUploadAsync(
        Guid ideaId,
        InitUploadRequest request,
        BandContext bandContext,
        AppDbContext db,
        IFileStorage storage,
        IClock clock,
        EntitlementOptions entitlements,
        CancellationToken ct)
    {
        var idea = await db.SongIdeas.SingleOrDefaultAsync(i => i.Id == ideaId, ct);
        if (idea is null)
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
            var number = await db.DemoVersions.Where(v => v.SongIdeaId == ideaId).Select(v => (int?)v.Number).MaxAsync(ct) ?? 0;
            var version = new DemoVersion
            {
                BandId = idea.BandId,
                SongIdeaId = ideaId,
                Number = number + 1,
                FileKey = $"bands/{idea.BandId}/ideas/{ideaId}/versions/{Guid.CreateVersion7()}{UploadRules.Extension(request.FileName)}",
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
                // Lost the (SongIdeaId, Number) race — recompute once.
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
            await notifier.DemoChangedAsync(version.BandId, version.SongIdeaId, "version", ct);
        }

        var uploaderName = await db.Memberships
            .Where(m => m.Id == version.UploadedByMembershipId)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => u.DisplayName)
            .SingleOrDefaultAsync(ct) ?? "?";
        return TypedResults.Ok(new VersionResponse(
            version.Id, version.Number, version.Kind, version.FileName, version.ContentType,
            version.SizeBytes, version.DurationSeconds, version.Peaks, uploaderName, version.CreatedAt));
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

        await notifier.DemoChangedAsync(version.BandId, version.SongIdeaId, "versionDeleted", ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<InitStemUploadResponse>, NotFound, ValidationProblem>> InitStemUploadAsync(
        Guid ideaId,
        InitStemUploadRequest request,
        BandContext bandContext,
        AppDbContext db,
        IFileStorage storage,
        IClock clock,
        EntitlementOptions entitlements,
        CancellationToken ct)
    {
        var idea = await db.SongIdeas.SingleOrDefaultAsync(i => i.Id == ideaId, ct);
        if (idea is null)
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
            BandId = idea.BandId,
            SongIdeaId = ideaId,
            Label = request.Label,
            Name = request.Name?.Trim(),
            FileKey = $"bands/{idea.BandId}/ideas/{ideaId}/stems/{Guid.CreateVersion7()}{UploadRules.Extension(request.FileName)}",
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
            await notifier.DemoChangedAsync(stem.BandId, stem.SongIdeaId, "stem", ct);
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

        await notifier.DemoChangedAsync(stem.BandId, stem.SongIdeaId, "stemDeleted", ct);
        return TypedResults.NoContent();
    }
}
