using Bandroom.Domain.Scheduling;
using NodaTime;

namespace Bandroom.Api.Data;

public enum BandPlan
{
    Free = 0,
    Pro = 1,
}

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

    /// <summary>Entitlements are per band, not per user (spec principle 4) —
    /// one member paying covers everyone. A provider webhook flips this later.</summary>
    public BandPlan Plan { get; set; } = BandPlan.Free;

    public Instant? PlanExpiresAt { get; set; }

    /// <summary>Verified bytes in object storage (demos + stems), quota-gated.</summary>
    public long StorageUsedBytes { get; set; }

    public required Instant CreatedAt { get; init; }
}
