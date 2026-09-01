using NodaTime;

namespace Bandroom.Domain.Scheduling;

/// <summary>
/// A member's standing availability: the day-part slots that generally work for
/// them, set once (spec §4). Anything outside the pattern counts as unavailable —
/// that is what makes silence meaningful in the finder.
/// </summary>
public sealed class WeeklyPattern
{
    private readonly HashSet<(IsoDayOfWeek Day, PracticeSlot Slot)> _open;

    public WeeklyPattern(IEnumerable<(IsoDayOfWeek Day, PracticeSlot Slot)> openSlots)
    {
        _open = [.. openSlots];
    }

    public static WeeklyPattern Empty { get; } = new([]);

    public static WeeklyPattern Evenings(params IsoDayOfWeek[] days) =>
        new(days.Select(day => (day, PracticeSlot.Evening)));

    public bool IsOpen(IsoDayOfWeek day, PracticeSlot slot) => _open.Contains((day, slot));
}
