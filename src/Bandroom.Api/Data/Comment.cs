using NodaTime;

namespace Bandroom.Api.Data;

public enum CommentTarget
{
    Event = 0,
    DemoVersion = 1,
}

/// <summary>
/// One table serves events and demo versions (spec §7) — decisions stay attached
/// to the thing they're about. AtSeconds renders as a waveform pin on versions.
/// </summary>
public sealed class Comment : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BandId { get; init; }

    public required CommentTarget TargetType { get; init; }

    public required Guid TargetId { get; init; }

    public required Guid AuthorMembershipId { get; init; }

    public required string Body { get; set; }

    public double? AtSeconds { get; init; }

    public required Instant CreatedAt { get; init; }
}
