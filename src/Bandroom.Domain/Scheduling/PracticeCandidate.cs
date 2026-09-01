using NodaTime;

namespace Bandroom.Domain.Scheduling;

/// <summary>
/// A day the band could practice, with exactly who is free. The finder only ever
/// proposes candidates — confirmation is a human RSVP (spec principle 2).
/// </summary>
public sealed record PracticeCandidate(
    LocalDate Date,
    PracticeSlot Slot,
    IReadOnlyList<Guid> AvailableMembershipIds,
    int TotalMembers)
{
    public int AvailableCount => AvailableMembershipIds.Count;

    public bool EveryoneAvailable => AvailableCount == TotalMembers;
}
