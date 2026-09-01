using NodaTime;

namespace Bandroom.Domain.Scheduling;

/// <summary>
/// One member's availability inputs for the finder: their weekly pattern plus
/// dated exceptions. Blockout intervals are inclusive at both ends — "can't the
/// 12th–15th" blocks the 15th too.
/// </summary>
public sealed record MemberAvailability(
    Guid MembershipId,
    WeeklyPattern Pattern,
    IReadOnlyList<DateInterval> Blockouts)
{
    public bool IsAvailable(LocalDate date, PracticeSlot slot) =>
        Pattern.IsOpen(date.DayOfWeek, slot) && !Blockouts.Any(blockout => blockout.Contains(date));
}
