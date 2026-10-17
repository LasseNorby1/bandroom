using NodaTime;

namespace Bandroom.Api.Data;

/// <summary>"Who brings what" on an event: one line, a name, a tick (spec §2, borrowed light).</summary>
public sealed class EventItem : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid BandId { get; init; }
    public required Guid EventId { get; init; }
    public required string Text { get; set; }
    public Guid? AssigneeMembershipId { get; set; }
    public bool Done { get; set; }
    public required Guid CreatedByMembershipId { get; init; }
    public required Instant CreatedAt { get; init; }
}

/// <summary>A setlist lives on a gig only: ordered songs from the repertoire, nothing standalone.</summary>
public sealed class SetlistEntry : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid BandId { get; init; }
    public required Guid EventId { get; init; }
    public required Guid SongId { get; init; }
    public required int Position { get; set; }
}

/// <summary>🔥 on a song — the same one signal as on versions, so the repertoire can sort by it.</summary>
public sealed class SongReaction : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid BandId { get; init; }
    public required Guid SongId { get; init; }
    public required Guid MembershipId { get; init; }
    public required Instant CreatedAt { get; init; }
}
