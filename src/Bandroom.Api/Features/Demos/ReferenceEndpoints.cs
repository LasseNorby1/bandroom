using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Infrastructure;
using Bandroom.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Demos;

public sealed record InitReferenceUploadRequest(string Title, string FileName, string ContentType, long SizeBytes);

public sealed record InitReferenceUploadResponse(Guid ReferenceId, string UploadUrl, DateTimeOffset ExpiresAt);

public sealed record ReferenceResponse(
    Guid Id, string Title, string FileName, long SizeBytes, VersionStatus Status, Instant CreatedAt);

public static class ReferenceEndpoints
{
    public static IEndpointRouteBuilder MapReferenceEndpoints(this IEndpointRouteBuilder routes)
    {
        var references = routes.MapGroup("/bands/{bandId:guid}/references")
            .RequireAuthorization()
            .WithTags("demos")
            .AddEndpointFilter<BandMemberFilter>();

        references.MapGet("/", ListAsync).WithSummary("The band's reference library");
        references.MapPost("/uploads", InitUploadAsync).WithSummary("Start a reference upload");
        references.MapPost("/{referenceId:guid}/confirm", ConfirmAsync).WithSummary("Finish a reference upload");
        references.MapDelete("/{referenceId:guid}", DeleteAsync).WithSummary("Delete a reference (uploader/admin)");

        return routes;
    }

    private static async Task<Ok<List<ReferenceResponse>>> ListAsync(AppDbContext db, CancellationToken ct)
    {
        var references = await db.ReferenceTracks
            .Where(r => r.Status == VersionStatus.Ready)
            .OrderBy(r => r.Title)
            .Select(r => new ReferenceResponse(r.Id, r.Title, r.FileName, r.SizeBytes, r.Status, r.CreatedAt))
            .ToListAsync(ct);
        return TypedResults.Ok(references);
    }

    private static async Task<Results<Ok<InitReferenceUploadResponse>, ValidationProblem>> InitUploadAsync(
        InitReferenceUploadRequest request,
        BandContext bandContext,
        AppDbContext db,
        IFileStorage storage,
        IClock clock,
        EntitlementOptions entitlements,
        CancellationToken ct)
    {
        var now = clock.GetCurrentInstant();
        var band = bandContext.Band;
        var errors = UploadRules.Validate(request.FileName, request.ContentType, request.SizeBytes, band, now, entitlements)
                     ?? new Dictionary<string, string[]>();
        var title = request.Title?.Trim() ?? "";
        if (title.Length is 0 or > 120)
        {
            errors["title"] = ["Title is required (max 120 characters)."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var reference = new ReferenceTrack
        {
            BandId = bandContext.BandId!.Value,
            Title = title,
            FileKey = $"bands/{bandContext.BandId}/references/{Guid.CreateVersion7()}{UploadRules.Extension(request.FileName)}",
            FileName = request.FileName.Trim(),
            ContentType = request.ContentType,
            UploadedByMembershipId = bandContext.MembershipId!.Value,
            CreatedAt = now,
        };
        db.ReferenceTracks.Add(reference);
        await db.SaveChangesAsync(ct);

        var upload = await storage.CreateUploadAsync(reference.FileKey, reference.ContentType, ct);
        return TypedResults.Ok(new InitReferenceUploadResponse(reference.Id, upload.Url, upload.ExpiresAt));
    }

    private static async Task<Results<Ok<ReferenceResponse>, NotFound, ValidationProblem>> ConfirmAsync(
        Guid referenceId,
        BandContext bandContext,
        AppDbContext db,
        IFileStorage storage,
        CancellationToken ct)
    {
        var reference = await db.ReferenceTracks.SingleOrDefaultAsync(r => r.Id == referenceId, ct);
        if (reference is null)
        {
            return TypedResults.NotFound();
        }

        if (reference.Status == VersionStatus.Uploading)
        {
            var size = await storage.GetSizeAsync(reference.FileKey, ct);
            if (size is null or 0)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["upload"] = ["The file hasn't arrived in storage — upload it first, then confirm."],
                });
            }

            reference.SizeBytes = size.Value;
            reference.Status = VersionStatus.Ready;
            bandContext.Band.StorageUsedBytes += size.Value;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(new ReferenceResponse(
            reference.Id, reference.Title, reference.FileName, reference.SizeBytes, reference.Status, reference.CreatedAt));
    }

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult>> DeleteAsync(
        Guid referenceId,
        BandContext bandContext,
        AppDbContext db,
        IFileStorage storage,
        CancellationToken ct)
    {
        var reference = await db.ReferenceTracks.SingleOrDefaultAsync(r => r.Id == referenceId, ct);
        if (reference is null)
        {
            return TypedResults.NotFound();
        }

        if (bandContext.Role != BandRole.Admin && reference.UploadedByMembershipId != bandContext.MembershipId)
        {
            return TypedResults.Forbid();
        }

        var band = bandContext.Band;
        band.StorageUsedBytes = Math.Max(0, band.StorageUsedBytes - reference.SizeBytes);
        db.ReferenceTracks.Remove(reference);
        await db.SaveChangesAsync(ct);

        try
        {
            await storage.DeleteAsync(reference.FileKey, ct);
        }
        catch
        {
            // Orphaned objects beat failed deletes; a sweep can reconcile later.
        }

        return TypedResults.NoContent();
    }
}
