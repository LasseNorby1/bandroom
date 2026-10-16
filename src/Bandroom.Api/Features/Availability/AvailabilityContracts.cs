using Bandroom.Api.Data;
using NodaTime;

namespace Bandroom.Api.Features.Availability;

public sealed record UpdatePatternRequest(IReadOnlyList<WeeklySlot> OpenSlots);

public sealed record CreateExceptionRequest(LocalDate From, LocalDate To, string? Note);

public sealed record ExceptionResponse(Guid Id, LocalDate From, LocalDate To, string? Note);

public sealed record BusyDayResponse(LocalDate Date, Bandroom.Domain.Scheduling.PracticeSlot? Slot, string? Summary);

public sealed record CalendarImportRequest(string? Url);

public sealed record CalendarImportStatus(string? Url, Instant? ImportedAt, string? Error, int BusyDays);

public sealed record MyAvailabilityResponse(
    IReadOnlyList<WeeklySlot> OpenSlots,
    Instant? PatternUpdatedAt,
    IReadOnlyList<ExceptionResponse> Exceptions,
    string CalendarFeedPath,
    CalendarImportStatus CalendarImport,
    IReadOnlyList<BusyDayResponse> ImportedBusy);

public sealed record MemberAvailabilityResponse(
    Guid MembershipId,
    string DisplayName,
    IReadOnlyList<WeeklySlot> OpenSlots,
    Instant? PatternUpdatedAt,
    IReadOnlyList<ExceptionResponse> Exceptions,
    IReadOnlyList<BusyDayResponse> ImportedBusy);

public sealed record BandAvailabilityResponse(
    LocalDate From,
    int Days,
    IReadOnlyList<MemberAvailabilityResponse> Members);
