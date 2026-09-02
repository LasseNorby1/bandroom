using Bandroom.Domain.Scheduling;
using NodaTime;

namespace Bandroom.Api.Data;

public enum BandRole
{
    Member = 0,
    Admin = 1,
}

/// <summary>One open day-part in a member's weekly pattern; stored as jsonb.</summary>
public sealed record WeeklySlot(IsoDayOfWeek Day, PracticeSlot Slot);

/// <summary>
/// The workhorse join (spec §7): you-in-this-band. Roles live here, not on the
/// user — and so does the weekly availability pattern, because availability is
/// also a property of you-in-this-band.
/// </summary>
public sealed class Membership : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BandId { get; init; }

    public required Guid UserId { get; init; }

    public required BandRole Role { get; set; }

    public string? Instrument { get; set; }

    public required Instant JoinedAt { get; init; }

    /// <summary>The standing "evenings that generally work" set (spec §4).</summary>
    public List<WeeklySlot> WeeklyPattern { get; set; } = [];

    /// <summary>Staleness signal for the finder ("Sofie's pattern is 5 weeks old").</summary>
    public Instant? PatternUpdatedAt { get; set; }

    /// <summary>
    /// Capability token for this member's personal calendar feed. Stored raw —
    /// unlike credentials it must be re-displayable, and it only grants read
    /// access to calendar data. Regenerable later.
    /// </summary>
    public required string IcsToken { get; init; }
}
