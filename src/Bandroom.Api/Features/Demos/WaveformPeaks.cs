namespace Bandroom.Api.Features.Demos;

/// <summary>
/// Peaks are client-supplied (the uploader's browser, or the worker), so they
/// are sanitised, never trusted: bounded in count, clamped to 0..1, NaN-free.
/// </summary>
public static class WaveformPeaks
{
    /// <summary>The web player draws this many bars; the worker emits the same.</summary>
    public const int BarCount = 96;

    public const int MaxCount = 256;

    public static float[]? Normalize(float[]? peaks)
    {
        if (peaks is null || peaks.Length == 0)
        {
            return null;
        }

        var bounded = peaks.Length > MaxCount ? peaks[..MaxCount] : peaks;
        var clean = new float[bounded.Length];
        for (var i = 0; i < bounded.Length; i++)
        {
            var value = bounded[i];
            clean[i] = float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
        }

        return clean;
    }
}
