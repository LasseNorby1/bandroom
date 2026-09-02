using Bandroom.Api.Data;
using Bandroom.Api.Infrastructure;
using NodaTime;

namespace Bandroom.Api.Features.Demos;

/// <summary>Shared validation for every audio upload (versions, stems, references).</summary>
public static class UploadRules
{
    public static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/mpeg", "audio/mp3", "audio/mp4", "audio/x-m4a", "audio/aac",
        "audio/wav", "audio/x-wav", "audio/wave", "audio/flac", "audio/ogg", "audio/webm",
    };

    public static Dictionary<string, string[]>? Validate(
        string fileName, string contentType, long sizeBytes, Band band, Instant now, EntitlementOptions entitlements)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 200)
        {
            errors["fileName"] = ["File name is required (max 200 characters)."];
        }

        if (!AllowedContentTypes.Contains(contentType))
        {
            errors["contentType"] = ["Audio only: mp3, m4a/aac, wav, flac, ogg or webm."];
        }

        if (sizeBytes is <= 0 or > PlanLimits.MaxUploadBytes)
        {
            errors["sizeBytes"] = [$"File size must be positive and at most {PlanLimits.MaxUploadBytes / (1024 * 1024)} MB."];
        }
        else if (band.StorageUsedBytes + sizeBytes > PlanLimits.StorageQuota(band, now, entitlements))
        {
            errors["sizeBytes"] = ["That would exceed the band's storage quota."];
        }

        return errors.Count > 0 ? errors : null;
    }

    public static string Extension(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return ext.Length is > 1 and <= 10 ? ext.ToLowerInvariant() : "";
    }
}
