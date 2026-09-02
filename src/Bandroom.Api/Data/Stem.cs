using NodaTime;

namespace Bandroom.Api.Data;

public enum StemLabel
{
    Drums = 0,
    Bass = 1,
    Guitar = 2,
    Keys = 3,
    Vocals = 4,
    Other = 5,
}

/// <summary>
/// One labelled track of a song idea — the input to ai demo polish. Same
/// two-phase upload discipline as demo versions.
/// </summary>
public sealed class Stem : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BandId { get; init; }

    public required Guid SongIdeaId { get; init; }

    public required StemLabel Label { get; init; }

    /// <summary>Free-text refinement of the label ("lead gtr").</summary>
    public string? Name { get; init; }

    public required string FileKey { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public long SizeBytes { get; set; }

    public VersionStatus Status { get; set; } = VersionStatus.Uploading;

    public required Guid UploadedByMembershipId { get; init; }

    public required Instant CreatedAt { get; init; }
}

public enum PolishStatus
{
    Queued = 0,
    Processing = 1,
    Done = 2,
    Failed = 3,
}

/// <summary>
/// One "mix &amp; master my stems" run. The roadie can run the desk, never write
/// the song (spec §5.5): output is a new DemoVersion of kind AiMix.
/// </summary>
public sealed class PolishJob : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BandId { get; init; }

    public required Guid SongIdeaId { get; init; }

    public PolishStatus Status { get; set; } = PolishStatus.Queued;

    /// <summary>At most one of these — a library track or any demo version in the band.</summary>
    public Guid? ReferenceTrackId { get; init; }

    public Guid? ReferenceVersionId { get; init; }

    public Guid? OutputVersionId { get; set; }

    public string? Error { get; set; }

    public required Guid RequestedByMembershipId { get; init; }

    public required Instant CreatedAt { get; init; }

    public Instant? CompletedAt { get; set; }
}
