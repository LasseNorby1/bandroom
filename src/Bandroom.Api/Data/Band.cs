using Bandroom.Domain.Scheduling;
using NodaTime;

namespace Bandroom.Api.Data;

/// <summary>
/// The tenant root. Not IBandScoped itself — access to a band is mediated by
/// having a membership in it, which the BandMemberFilter enforces per request.
/// </summary>
public sealed class Band
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// <summary>IANA id (e.g. Europe/Copenhagen) — all slot math runs band-local.</summary>
    public required string TimeZone { get; set; }

    /// <summary>Minimum members for a viable practice (spec §4).</summary>
    public int Quorum { get; set; } = 2;

    public PracticeSlot DefaultPracticeSlot { get; set; } = PracticeSlot.Evening;

    public string? RehearsalSpace { get; set; }

    public required Instant CreatedAt { get; init; }
}
