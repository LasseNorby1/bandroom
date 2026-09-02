using Bandroom.Api.Data;
using Bandroom.Domain.Scheduling;
using NodaTime;

namespace Bandroom.Api.Features.Bands;

public sealed record CreateBandRequest(string Name, string? TimeZone);

public sealed record UpdateBandRequest(
    string? Name,
    string? TimeZone,
    int? Quorum,
    PracticeSlot? DefaultPracticeSlot,
    string? RehearsalSpace);

public sealed record BandResponse(
    Guid Id,
    string Name,
    string TimeZone,
    int Quorum,
    PracticeSlot DefaultPracticeSlot,
    string? RehearsalSpace,
    BandPlan Plan,
    long StorageUsedBytes,
    long StorageQuotaBytes);

public sealed record BandSummaryResponse(Guid Id, string Name, BandRole Role);

public sealed record MemberResponse(
    Guid MembershipId,
    Guid UserId,
    string DisplayName,
    BandRole Role,
    string? Instrument,
    Instant JoinedAt);

public sealed record BandDetailResponse(BandResponse Band, IReadOnlyList<MemberResponse> Members);

public sealed record CreateInviteRequest(int? MaxUses, int? ExpiresInDays);

public sealed record InviteCreatedResponse(Guid Id, string Token, Instant ExpiresAt, int? MaxUses);

public sealed record InviteResponse(Guid Id, Instant CreatedAt, Instant ExpiresAt, int UseCount, int? MaxUses);

public sealed record AcceptInviteResponse(Guid BandId, string BandName, Guid MembershipId);
