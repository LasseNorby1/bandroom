using NodaTime;

namespace Bandroom.Api.Data;

/// <summary>
/// The roadie's draft of a song's lyrics and chords, heard from one take
/// (spec §5.5). Derived data: one per song, regenerable, never the song's own
/// chord sheet until a member says "use it".
/// </summary>
public sealed class SheetDraft : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BandId { get; init; }

    public required Guid SongId { get; init; }

    public required Guid VersionId { get; set; }

    public AnalysisStatus Status { get; set; } = AnalysisStatus.Queued;

    public string? Language { get; set; }

    /// <summary>ChordPro text, as the api built it from the worker's two timelines.</summary>
    public string? Draft { get; set; }

    public string? Model { get; set; }

    public string? Error { get; set; }

    public required Guid RequestedByMembershipId { get; init; }

    public required Instant CreatedAt { get; set; }

    public Instant? CompletedAt { get; set; }
}
