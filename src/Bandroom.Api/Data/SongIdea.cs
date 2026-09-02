using NodaTime;

namespace Bandroom.Api.Data;

public enum IdeaStatus
{
    Idea = 0,
    InProgress = 1,
    Finished = 2,
    Parked = 3,
}

/// <summary>
/// The container demos live in: "bridge idea v3" beats fourteen files named
/// new_song_final_2.m4a (spec §5.2). Versions and stems hang off it.
/// </summary>
public sealed class SongIdea : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BandId { get; init; }

    public required string Title { get; set; }

    public IdeaStatus Status { get; set; } = IdeaStatus.Idea;

    /// <summary>Optional link into the repertoire.</summary>
    public Guid? SongId { get; set; }

    public required Guid CreatedByMembershipId { get; init; }

    public required Instant CreatedAt { get; init; }
}
