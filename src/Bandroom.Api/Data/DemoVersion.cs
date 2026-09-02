using NodaTime;

namespace Bandroom.Api.Data;

public enum VersionStatus
{
    Uploading = 0,
    Ready = 1,
    Failed = 2,
}

public enum VersionKind
{
    Upload = 0,
    AiMix = 1,
}

/// <summary>
/// One take under a song idea. Two-phase upload: a row is created with the
/// presigned url (Uploading), then confirm verifies the object exists and its
/// real size before the version becomes visible (Ready).
/// </summary>
public sealed class DemoVersion : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BandId { get; init; }

    public required Guid SongIdeaId { get; init; }

    /// <summary>Per-idea sequence (v1, v2, …).</summary>
    public required int Number { get; set; }

    public required string FileKey { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    /// <summary>Verified at confirm time from storage — never the declared value.</summary>
    public long SizeBytes { get; set; }

    public double? DurationSeconds { get; set; }

    public VersionStatus Status { get; set; } = VersionStatus.Uploading;

    public VersionKind Kind { get; init; } = VersionKind.Upload;

    public required Guid UploadedByMembershipId { get; init; }

    public required Instant CreatedAt { get; init; }
}
