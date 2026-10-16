using Bandroom.Domain.Scheduling;
using NodaTime;

namespace Bandroom.Api.Features.Scheduling;

public sealed record FinderMemberDto(Guid MembershipId, string DisplayName);

public sealed record FinderCandidateResponse(
    LocalDate Date,
    PracticeSlot Slot,
    int AvailableCount,
    int TotalMembers,
    bool EveryoneAvailable,
    IReadOnlyList<FinderMemberDto> AvailableMembers);

/// <summary>Staleness input for the ui ("Sofie's pattern is 5 weeks old", "Jonas hasn't set one").</summary>
public sealed record FinderMemberStatusDto(
    Guid MembershipId,
    string DisplayName,
    int OpenSlots,
    Instant? PatternUpdatedAt);

public sealed record PracticeFinderResponse(
    LocalDate From,
    int Days,
    int Quorum,
    PracticeSlot Slot,
    IReadOnlyList<FinderCandidateResponse> Candidates,
    IReadOnlyList<FinderMemberStatusDto> Members);
