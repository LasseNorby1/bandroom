using Bandroom.Api.Data;
using NodaTime;

namespace Bandroom.Api.Infrastructure;

public sealed record EntitlementOptions
{
    /// <summary>Dev/dogfood switch — treats every band as pro.</summary>
    public bool EveryBandPro { get; init; }
}

public static class PlanLimits
{
    public const long FreeStorageBytes = 1L * 1024 * 1024 * 1024;   // 1 GiB
    public const long ProStorageBytes = 25L * 1024 * 1024 * 1024;   // 25 GiB
    public const long MaxUploadBytes = 300L * 1024 * 1024;          // one file

    public static bool IsPro(Band band, Instant now, EntitlementOptions entitlements) =>
        entitlements.EveryBandPro ||
        (band.Plan == BandPlan.Pro && (band.PlanExpiresAt is null || band.PlanExpiresAt > now));

    public static long StorageQuota(Band band, Instant now, EntitlementOptions entitlements) =>
        IsPro(band, now, entitlements) ? ProStorageBytes : FreeStorageBytes;
}
