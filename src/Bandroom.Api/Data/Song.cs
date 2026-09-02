using NodaTime;

namespace Bandroom.Api.Data;

public enum SongStatus
{
    Active = 0,
    Retired = 1,
}

/// <summary>The band's repertoire — demo ideas link into it (spec §5, "later" modules).</summary>
public sealed class Song : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BandId { get; init; }

    public required string Title { get; set; }

    /// <summary>Musical key as free text ("F#m") — display only.</summary>
    public string? Key { get; set; }

    public int? Bpm { get; set; }

    public SongStatus Status { get; set; } = SongStatus.Active;

    public string? Notes { get; set; }

    public required Instant CreatedAt { get; init; }
}
