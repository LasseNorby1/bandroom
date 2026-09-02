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

public sealed record PracticeFinderResponse(
    LocalDate From,
    int Days,
    int Quorum,
    PracticeSlot Slot,
    IReadOnlyList<FinderCandidateResponse> Candidates);
