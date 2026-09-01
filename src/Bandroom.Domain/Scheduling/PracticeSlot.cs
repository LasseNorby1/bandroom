namespace Bandroom.Domain.Scheduling;

/// <summary>
/// Bands practice in day-part blocks, not clock hours — availability is tracked
/// at this granularity by design (spec §4). Concrete start/end times are applied
/// when an event is materialised, in the band's timezone.
/// </summary>
public enum PracticeSlot
{
    Afternoon = 0,
    Evening = 1,
}
