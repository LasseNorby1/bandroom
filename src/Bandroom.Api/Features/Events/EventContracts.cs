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
    bool Confirmed = false,
    string? ContactName = null,
    string? ContactPhone = null,
    string? ContactEmail = null);

/// <summary>Creator/admin edits of the words on an event. Null leaves a field alone; "" clears it.</summary>
public sealed record UpdateEventRequest(
    string? Title = null,
    string? Location = null,
    string? ContactName = null,
    string? ContactPhone = null,
    string? ContactEmail = null);

public sealed record EventContactResponse(string? Name, string? Phone, string? Email);

/// <summary>"Who brings what": one line, maybe a name, a tick.</summary>
public sealed record EventItemResponse(Guid Id, string Text, Guid? AssigneeMembershipId, string? AssigneeName, bool Done);

public sealed record CreateEventItemRequest(string Text, Guid? AssigneeMembershipId = null);

public sealed record UpdateEventItemRequest(string? Text = null, Guid? AssigneeMembershipId = null, bool ClearAssignee = false, bool? Done = null);

/// <summary>A setlist entry: the song as it is in the repertoire, in order.</summary>
public sealed record SetlistItemResponse(Guid SongId, string Title, string? Key, int? Bpm, int? DurationSeconds);

public sealed record ReplaceSetlistRequest(IReadOnlyList<Guid> SongIds);

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
    int GoingCount,
    Guid CreatedByMembershipId,
    /// <summary>Confirmed but short of quorum after a late "can't" — the organiser was asked to keep or cancel.</summary>
    bool BelowQuorum,
    RsvpStatus? MyRsvp);

public sealed record EventDetailResponse(
    EventSummaryResponse Event,
    IReadOnlyList<RsvpResponse> Rsvps,
    EventContactResponse Contact,
    IReadOnlyList<EventItemResponse> Items,
    IReadOnlyList<SetlistItemResponse> Setlist,
    /// <summary>Sum of the setlist's song durations, for "about 45 min".</summary>
    int SetlistSeconds);
