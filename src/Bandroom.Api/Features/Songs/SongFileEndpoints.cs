using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Features.Demos;
using Bandroom.Api.Infrastructure;
using Bandroom.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Songs;

public sealed record InitSongFileUploadRequest(string Title, string FileName, string ContentType, long SizeBytes);

public sealed record InitSongFileUploadResponse(Guid FileId, string UploadUrl, DateTimeOffset ExpiresAt);

public sealed record SongFileResponse(
    Guid Id, Guid SongId, string Title, string FileName, string ContentType, long SizeBytes, string UploaderName, Instant CreatedAt);

public sealed record DownloadUrlResponse(string Url);

/// <summary>Lyrics, chord sheets and charts on a song — pdfs, text and images, up to 20 MB.</summary>
public static class SongFileEndpoints
{
    public const long MaxBytes = 20L * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "text/plain", "text/markdown", "image/png", "image/jpeg", "image/webp",
    };

    public static IEndpointRouteBuilder MapSongFileEndpoints(this IEndpointRouteBuilder routes)
    {
        var band = routes.MapGroup("/bands/{bandId:guid}")
            .RequireAuthorization()
            .WithTags("songs")
            .AddEndpointFilter<BandMemberFilter>();

        band.MapGet("/songs/{songId:guid}/files", ListAsync).WithSummary("Files on a song");
        band.MapPost("/songs/{songId:guid}/files/uploads", InitUploadAsync).WithSummary("Start a file upload (presigned url)");
        band.MapPost("/song-files/{fileId:guid}/confirm", ConfirmAsync).WithSummary("Finish a file upload");
        band.MapGet("/song-files/{fileId:guid}/download", DownloadAsync).WithSummary("Presigned download url");
        band.MapDelete("/song-files/{fileId:guid}", DeleteAsync).WithSummary("Delete a file (uploader/admin)");

        return routes;
    }

    private static async Task<Results<Ok<List<SongFileResponse>>, NotFound>> ListAsync(
        Guid songId, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Songs.AnyAsync(s => s.Id == songId, ct))
        {
            return TypedResults.NotFound();
        }

        var files = await db.SongFiles
            .Where(f => f.SongId == songId && f.Status == VersionStatus.Ready)
            .OrderBy(f => f.Title)
            .Join(db.Memberships, f => f.UploadedByMembershipId, m => m.Id, (f, m) => new { f, m })
            .Join(db.Users, x => x.m.UserId, u => u.Id, (x, u) => new SongFileResponse(
                x.f.Id, x.f.SongId, x.f.Title, x.f.FileName, x.f.ContentType, x.f.SizeBytes, u.DisplayName, x.f.CreatedAt))
            .ToListAsync(ct);
        return TypedResults.Ok(files);
    }

    private static async Task<Results<Ok<InitSongFileUploadResponse>, NotFound, ValidationProblem>> InitUploadAsync(
        Guid songId,
        InitSongFileUploadRequest request,
        BandContext bandContext,
        AppDbContext db,
        IFileStorage storage,
        IClock clock,
        EntitlementOptions entitlements,
        CancellationToken ct)
    {
        if (!await db.Songs.AnyAsync(s => s.Id == songId, ct))
        {
            return TypedResults.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        var title = request.Title?.Trim() ?? "";
        if (title.Length is 0 or > 120)
        {
            errors["title"] = ["Title is required (max 120 characters)."];
        }

        if (string.IsNullOrWhiteSpace(request.FileName) || request.FileName.Length > 200)
        {
            errors["fileName"] = ["File name is required (max 200 characters)."];
        }

        if (!AllowedContentTypes.Contains(request.ContentType))
        {
            errors["contentType"] = ["Pdf, plain text, markdown, png, jpeg or webp."];
        }

        var now = clock.GetCurrentInstant();
        var band = bandContext.Band;
        if (request.SizeBytes is <= 0 or > MaxBytes)
        {
            errors["sizeBytes"] = [$"File size must be positive and at most {MaxBytes / (1024 * 1024)} MB."];
        }
        else if (band.StorageUsedBytes + request.SizeBytes > PlanLimits.StorageQuota(band, now, entitlements))
        {
            errors["sizeBytes"] = ["That would exceed the band's storage quota."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var file = new SongFile
        {
            BandId = band.Id,
            SongId = songId,
            Title = title,
            FileKey = $"bands/{band.Id}/songs/{songId}/files/{Guid.CreateVersion7()}{UploadRules.Extension(request.FileName)}",
            FileName = request.FileName.Trim(),
            ContentType = request.ContentType,
            UploadedByMembershipId = bandContext.MembershipId!.Value,
            CreatedAt = now,
        };
        db.SongFiles.Add(file);
        await db.SaveChangesAsync(ct);

        var upload = await storage.CreateUploadAsync(file.FileKey, file.ContentType, ct);
        return TypedResults.Ok(new InitSongFileUploadResponse(file.Id, upload.Url, upload.ExpiresAt));
    }

    private static async Task<Results<Ok<SongFileResponse>, NotFound, ValidationProblem>> ConfirmAsync(
        Guid fileId,
        BandContext bandContext,
        AppDbContext db,
        IFileStorage storage,
        CancellationToken ct)
    {
        var file = await db.SongFiles.SingleOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null)
        {
            return TypedResults.NotFound();
        }

        if (file.Status == VersionStatus.Uploading)
        {
            var size = await storage.GetSizeAsync(file.FileKey, ct);
            if (size is null or 0)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["upload"] = ["The file hasn't arrived in storage — upload it first, then confirm."],
                });
            }

            file.SizeBytes = size.Value;
            file.Status = VersionStatus.Ready;
            bandContext.Band.StorageUsedBytes += size.Value;
            await db.SaveChangesAsync(ct);
        }

        var uploaderName = await db.Memberships
            .Where(m => m.Id == file.UploadedByMembershipId)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => u.DisplayName)
            .SingleOrDefaultAsync(ct) ?? "?";
        return TypedResults.Ok(new SongFileResponse(
            file.Id, file.SongId, file.Title, file.FileName, file.ContentType, file.SizeBytes, uploaderName, file.CreatedAt));
    }

    private static async Task<Results<Ok<DownloadUrlResponse>, NotFound>> DownloadAsync(
        Guid fileId, AppDbContext db, IFileStorage storage, CancellationToken ct)
    {
        var file = await db.SongFiles.SingleOrDefaultAsync(f => f.Id == fileId && f.Status == VersionStatus.Ready, ct);
        return file is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new DownloadUrlResponse(storage.GetDownloadUrl(file.FileKey, TimeSpan.FromHours(1))));
    }

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult>> DeleteAsync(
        Guid fileId,
        BandContext bandContext,
        AppDbContext db,
        IFileStorage storage,
        CancellationToken ct)
    {
        var file = await db.SongFiles.SingleOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null)
        {
            return TypedResults.NotFound();
        }

        if (bandContext.Role != BandRole.Admin && file.UploadedByMembershipId != bandContext.MembershipId)
        {
            return TypedResults.Forbid();
        }

        var band = bandContext.Band;
        band.StorageUsedBytes = Math.Max(0, band.StorageUsedBytes - file.SizeBytes);
        db.SongFiles.Remove(file);
        await db.SaveChangesAsync(ct);

        try
        {
            await storage.DeleteAsync(file.FileKey, ct);
        }
        catch
        {
            // Orphaned objects beat failed deletes; a sweep can reconcile later.
        }

        return TypedResults.NoContent();
    }
}
