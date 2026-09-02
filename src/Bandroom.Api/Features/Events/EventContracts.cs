using Bandroom.Api.Data;
using Bandroom.Domain.Scheduling;
using NodaTime;

namespace Bandroom.Api.Features.Events;

public sealed record CreateEventRequest(
    EventType Type,
    LocalDate Date,
    PracticeSlot? Slot = null,
    LocalTime? StartTime = null,
    LocalTime? EndTime = null,
    string? Title = null,
    string? Location = null,
    bool Confirmed = false);

public sealed record RsvpRequest(RsvpStatus Status, string? Note = null);

public sealed record RsvpResponse(Guid MembershipId, string DisplayName, RsvpStatus Status, string? Note);

public sealed record EventSummaryResponse(
    Guid Id,
    EventType Type,
    EventStatus Status,
    LocalDate Date,
    PracticeSlot? Slot,
    LocalTime? StartTime,
    LocalTime? EndTime,
    string? Title,
    string? Location,
    int GoingCount);

public sealed record EventDetailResponse(EventSummaryResponse Event, IReadOnlyList<RsvpResponse> Rsvps);
