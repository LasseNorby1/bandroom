using Bandroom.Api.Data;
using Bandroom.Domain.Scheduling;
using NodaTime;

namespace Bandroom.Api.Features.Events;

/// <summary>Shared human-readable rendering for ics, reminders and digests.</summary>
public static class EventText
{
    /// <summary>"thu 14 nov · 19–22" — band-local, for notifications.</summary>
    public static string When(Event evt, Band band)
    {
        var (start, end) = SlotTimes.Resolve(evt);
        return $"{evt.Date:ddd d MMM} · {start:HH:mm}–{end:HH:mm}";
    }

    public static string Summarize(Event evt, Band band) => evt.Title ?? evt.Type switch
    {
        EventType.Practice => $"Practice — {band.Name}",
        EventType.Gig => $"Gig — {band.Name}",
        EventType.Deadline => $"Deadline — {band.Name}",
        _ => band.Name,
    };
}

/// <summary>
/// Day-part slots become concrete times only here, at materialisation (ics,
/// reminders) — availability itself never needs clock precision (spec §4).
/// Band-configurable slot times can replace these constants later.
/// </summary>
public static class SlotTimes
{
    public static readonly LocalTime AfternoonStart = new(14, 0);
    public static readonly LocalTime AfternoonEnd = new(17, 0);
    public static readonly LocalTime EveningStart = new(19, 0);
    public static readonly LocalTime EveningEnd = new(22, 0);

    public static (LocalTime Start, LocalTime End) Resolve(Event evt)
    {
        if (evt.StartTime is { } start)
        {
            return (start, evt.EndTime ?? start.PlusHours(3));
        }

        return (evt.Slot ?? PracticeSlot.Evening) == PracticeSlot.Afternoon
            ? (AfternoonStart, AfternoonEnd)
            : (EveningStart, EveningEnd);
    }
}
