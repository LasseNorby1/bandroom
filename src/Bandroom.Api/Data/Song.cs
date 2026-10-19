using NodaTime;

namespace Bandroom.Api.Data;

/// <summary>Where a song is in its life. The only sorting the songs page needs; nobody files anything.</summary>
public enum SongStatus
{
    /// <summary>A riff, a voice memo, a title — not a song yet.</summary>
    Idea = 0,
    /// <summary>Being worked on at practice.</summary>
    Working = 1,
    /// <summary>In the repertoire: playable, setlist-able.</summary>
    Ready = 2,
    /// <summary>Shelved, kept.</summary>
    Parked = 3,
}

/// <summary>
/// The one container (spec §5.2, revised oct 2026): a song from first riff to
/// repertoire. Takes, stems, the chord sheet, files and 🔥 all hang off it.
/// There is no separate "idea" — an idea is a song with status Idea.
/// </summary>
public sealed class Song : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BandId { get; init; }

    public required string Title { get; set; }

    /// <summary>Musical key as free text ("F#m") — display only.</summary>
    public string? Key { get; set; }

    public int? Bpm { get; set; }

    /// <summary>How long it runs live — the setlist's total comes from this.</summary>
    public int? DurationSeconds { get; set; }

    /// <summary>Lyrics with inline chords, ChordPro style: "[Am]Hold the [F]line". Pasted, rendered, never parsed further.</summary>
    public string? Chords { get; set; }

    public SongStatus Status { get; set; } = SongStatus.Idea;

    public string? Notes { get; set; }

    /// <summary>Who started it. Null for songs that predate the field.</summary>
    public Guid? CreatedByMembershipId { get; init; }

    public required Instant CreatedAt { get; init; }
}
