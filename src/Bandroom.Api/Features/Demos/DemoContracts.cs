using Bandroom.Api.Data;
using NodaTime;

namespace Bandroom.Api.Features.Demos;

public sealed record InitUploadRequest(string FileName, string ContentType, long SizeBytes);

public sealed record InitUploadResponse(Guid VersionId, string UploadUrl, DateTimeOffset ExpiresAt);

public sealed record ConfirmUploadRequest(double? DurationSeconds, float[]? Peaks = null);

public sealed record VersionResponse(
    Guid Id,
    int Number,
    VersionKind Kind,
    string FileName,
    string ContentType,
    long SizeBytes,
    double? DurationSeconds,
    float[]? Peaks,
    string UploaderName,
    Instant CreatedAt,
    int FireCount,
    bool FiredByMe,
    double? Bpm,
    string? Key);

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

/// <summary>The takes side of a song: what hangs off it in audio.</summary>
public sealed record SongTakesResponse(
    IReadOnlyList<VersionResponse> Versions,
    IReadOnlyList<StemResponse> Stems,
    IReadOnlyList<PolishJobResponse> PolishJobs);

public sealed record StreamUrlResponse(string Url);
