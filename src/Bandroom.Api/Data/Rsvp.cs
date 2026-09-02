using NodaTime;

namespace Bandroom.Api.Data;

public enum RsvpStatus
{
    Going = 0,
    NotGoing = 1,
    Maybe = 2,
}

/// <summary>
/// The truth-check that makes the low-effort availability model safe (spec
/// principle 2) — computed availability proposes, this confirms.
/// </summary>
public sealed class Rsvp : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BandId { get; init; }

    public required Guid EventId { get; init; }

    public required Guid MembershipId { get; init; }

    public required RsvpStatus Status { get; set; }

    public string? Note { get; set; }

    public required Instant UpdatedAt { get; set; }
}
