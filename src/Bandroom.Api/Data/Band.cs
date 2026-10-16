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

    /// <summary>Band-local hour (0–23) after which the day-before reminder goes out.</summary>
    public int ReminderHour { get; set; } = 17;

    /// <summary>The one pinned note (spec §5.3) — "gig 12/6, load-in 17:00", "new code for the door".</summary>
    public string? Notice { get; set; }

    public Instant? NoticeUpdatedAt { get; set; }

    public Guid? NoticeByMembershipId { get; set; }

    /// <summary>
    /// A standing practice day ("we play thursdays"): the recurring-practice job
    /// proposes it a fortnight ahead so the rsvp round still happens.
    /// </summary>
    public IsoDayOfWeek? RecurringPracticeDay { get; set; }

    /// <summary>Entitlements are per band, not per user (spec principle 4) —
    /// one member paying covers everyone. A provider webhook flips this later.</summary>
    public BandPlan Plan { get; set; } = BandPlan.Free;

    public Instant? PlanExpiresAt { get; set; }

    /// <summary>Merchant-of-record references, filled in by the billing webhook.</summary>
    public string? BillingCustomerId { get; set; }

    public string? BillingSubscriptionId { get; set; }

    public string? BillingPortalUrl { get; set; }

    /// <summary>Per-band ai switch (spec §5.5, trust): parsing, drafting, summaries — never the music.</summary>
    public bool AiEnabled { get; set; } = true;

    /// <summary>Verified bytes in object storage (demos + stems), quota-gated.</summary>
    public long StorageUsedBytes { get; set; }

    public required Instant CreatedAt { get; init; }
}
