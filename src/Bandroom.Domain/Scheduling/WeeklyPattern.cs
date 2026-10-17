using NodaTime;

namespace Bandroom.Domain.Scheduling;

/// <summary>
/// A member's standing availability: the day-part slots that generally work for
/// them, set once (spec §4). Anything outside the pattern counts as unavailable —
/// that is what makes silence meaningful in the finder.
///
/// A pattern may alternate: every other week a second set of slots applies
/// (the 7/7 custody case). The alternation is anchored to a Monday on which the
/// primary set applies — never to iso week parity, which flips in 53-week years.
/// </summary>
public sealed class WeeklyPattern
{
    private readonly HashSet<(IsoDayOfWeek Day, PracticeSlot Slot)> _open;
    private readonly HashSet<(IsoDayOfWeek Day, PracticeSlot Slot)>? _alternate;
    private readonly LocalDate? _anchorMonday;

    public WeeklyPattern(IEnumerable<(IsoDayOfWeek Day, PracticeSlot Slot)> openSlots)
    {
        _open = [.. openSlots];
    }

    /// <param name="anchorMonday">A Monday whose week uses <paramref name="openSlots"/>; the next week uses <paramref name="alternateSlots"/>, and so on.</param>
    public WeeklyPattern(
        IEnumerable<(IsoDayOfWeek Day, PracticeSlot Slot)> openSlots,
        IEnumerable<(IsoDayOfWeek Day, PracticeSlot Slot)> alternateSlots,
        LocalDate anchorMonday)
    {
        if (anchorMonday.DayOfWeek != IsoDayOfWeek.Monday)
        {
            throw new ArgumentException("The anchor must be a Monday.", nameof(anchorMonday));
        }

        _open = [.. openSlots];
        _alternate = [.. alternateSlots];
        _anchorMonday = anchorMonday;
    }

    public static WeeklyPattern Empty { get; } = new([]);

    public static WeeklyPattern Evenings(params IsoDayOfWeek[] days) =>
        new(days.Select(day => (day, PracticeSlot.Evening)));

    public bool Alternates => _alternate is not null;

    /// <summary>The set in force on a date: primary on anchor-parity weeks, alternate on the others.</summary>
    public bool IsOpen(LocalDate date, PracticeSlot slot) =>
        UsesAlternateOn(date) ? _alternate!.Contains((date.DayOfWeek, slot)) : _open.Contains((date.DayOfWeek, slot));

    /// <summary>Date-less lookup: the primary set. Only meaningful for non-alternating patterns.</summary>
    public bool IsOpen(IsoDayOfWeek day, PracticeSlot slot) => _open.Contains((day, slot));

    public bool UsesAlternateOn(LocalDate date)
    {
        if (_anchorMonday is not { } anchor)
        {
            return false;
        }

        var monday = date.PlusDays(-((int)date.DayOfWeek - 1));
        var weeks = Period.Between(anchor, monday, PeriodUnits.Weeks).Weeks;
        return ((weeks % 2) + 2) % 2 == 1;
    }
}
