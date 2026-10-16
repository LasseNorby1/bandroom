using Bandroom.Domain.Scheduling;
using NodaTime;

namespace Bandroom.Api.Data;

/// <summary>
/// A day (and slot) a member's own calendar says they're busy — the opt-in
/// calendar overlay (spec §8 p4). Derived from the member's private ics url
/// by the import job and rewritten on every run; never user-authored.
/// </summary>
public sealed class ImportedBusyDay : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BandId { get; init; }

    public required Guid MembershipId { get; init; }

    public required LocalDate Date { get; init; }

    /// <summary>Null means the whole day (an all-day event).</summary>
    public PracticeSlot? Slot { get; init; }

    public string? Summary { get; init; }
}
