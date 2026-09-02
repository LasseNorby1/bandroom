using Bandroom.Api.Data;
using NodaTime;

namespace Bandroom.Api.Features.Demos;

public sealed record CreateIdeaRequest(string Title);

public sealed record UpdateIdeaRequest(string? Title, IdeaStatus? Status, Guid? SongId);

public sealed record IdeaSummaryResponse(
    Guid Id,
    string Title,
    IdeaStatus Status,
    Guid? SongId,
    int VersionCount,
    Instant? LatestVersionAt,
    Instant CreatedAt);

public sealed record InitUploadRequest(string FileName, string ContentType, long SizeBytes);

public sealed record InitUploadResponse(Guid VersionId, string UploadUrl, DateTimeOffset ExpiresAt);

public sealed record ConfirmUploadRequest(double? DurationSeconds);

public sealed record VersionResponse(
    Guid Id,
    int Number,
    VersionKind Kind,
    string FileName,
    string ContentType,
    long SizeBytes,
    double? DurationSeconds,
    string UploaderName,
    Instant CreatedAt);

public sealed record StemResponse(
    Guid Id,
    StemLabel Label,
    string? Name,
    string FileName,
    long SizeBytes,
    VersionStatus Status,
    Instant CreatedAt);

public sealed record InitStemUploadRequest(
    StemLabel Label, string? Name, string FileName, string ContentType, long SizeBytes);

public sealed record InitStemUploadResponse(Guid StemId, string UploadUrl, DateTimeOffset ExpiresAt);

public sealed record PolishJobResponse(
    Guid Id,
    PolishStatus Status,
    Guid? OutputVersionId,
    string? Error,
    Instant CreatedAt,
    Instant? CompletedAt);

public sealed record IdeaDetailResponse(
    Guid Id,
    string Title,
    IdeaStatus Status,
    Guid? SongId,
    IReadOnlyList<VersionResponse> Versions,
    IReadOnlyList<StemResponse> Stems,
    IReadOnlyList<PolishJobResponse> PolishJobs);

public sealed record StreamUrlResponse(string Url);
