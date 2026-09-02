using Bandroom.Api.Data;
using NodaTime;

namespace Bandroom.Api.Features.Availability;

public sealed record UpdatePatternRequest(IReadOnlyList<WeeklySlot> OpenSlots);

public sealed record CreateExceptionRequest(LocalDate From, LocalDate To, string? Note);

public sealed record ExceptionResponse(Guid Id, LocalDate From, LocalDate To, string? Note);

public sealed record MyAvailabilityResponse(
    IReadOnlyList<WeeklySlot> OpenSlots,
    Instant? PatternUpdatedAt,
    IReadOnlyList<ExceptionResponse> Exceptions,
    string CalendarFeedPath);

public sealed record MemberAvailabilityResponse(
    Guid MembershipId,
    string DisplayName,
    IReadOnlyList<WeeklySlot> OpenSlots,
    Instant? PatternUpdatedAt,
    IReadOnlyList<ExceptionResponse> Exceptions);

public sealed record BandAvailabilityResponse(
    LocalDate From,
    int Days,
    IReadOnlyList<MemberAvailabilityResponse> Members);
