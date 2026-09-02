using Bandroom.Domain.Scheduling;
using NodaTime;

namespace Bandroom.Api.Data;

public enum EventType
{
    Practice = 0,
    Gig = 1,
    Deadline = 2,
    Other = 3,
}

public enum EventStatus
{
    Proposed = 0,
    Confirmed = 1,
    Cancelled = 2,
}

/// <summary>
/// Proposed → confirmed → (cancelled) lifecycle (spec §5.1). Dates are band-local;
/// concrete times are optional — a practice usually rides its slot, and slot
/// defaults are applied when the event is materialised (ics, reminders).
/// </summary>
public sealed class Event : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BandId { get; init; }

    public required EventType Type { get; init; }

    public required EventStatus Status { get; set; }

    public required LocalDate Date { get; set; }

    public PracticeSlot? Slot { get; set; }

    public LocalTime? StartTime { get; set; }

    public LocalTime? EndTime { get; set; }

    public string? Title { get; set; }

    public string? Location { get; set; }

    public required Guid CreatedByMembershipId { get; init; }

    public required Instant CreatedAt { get; init; }

    /// <summary>Set by the reminder job so a day-before nudge is sent exactly once.</summary>
    public Instant? ReminderSentAt { get; set; }
}
