using NodaTime;

namespace Bandroom.Domain.Scheduling;

/// <param name="Slot">The band's default practice slot (spec §4).</param>
/// <param name="From">First date to consider, inclusive, in band-local terms.</param>
/// <param name="HorizonDays">How many days ahead to search.</param>
/// <param name="Quorum">Minimum members for a viable practice — a band setting.</param>
/// <param name="LastPractice">When set, days closer than <paramref name="MinDaysSinceLastPractice"/> are skipped.</param>
public sealed record PracticeFinderRequest(
    IReadOnlyList<MemberAvailability> Members,
    PracticeSlot Slot,
    LocalDate From,
    int HorizonDays,
    int Quorum,
    LocalDate? LastPractice = null,
    int MinDaysSinceLastPractice = 2);
