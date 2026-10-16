using NodaTime;

namespace Bandroom.Api.Data;

public enum AnalysisStatus
{
    Queued = 0,
    Done = 1,
    Failed = 2,
}

/// <summary>
/// Derived data for a demo version — bpm and key from the mir worker (spec
/// §5.5: ai never sits in the request path; derived tables keyed by source +
/// model version, disposable and regenerable).
/// </summary>
public sealed class VersionAnalysis : IBandScoped
{
    /// <summary>One analysis per version — the version id is the key.</summary>
    public required Guid VersionId { get; init; }

    public required Guid BandId { get; init; }

    public required string ModelVersion { get; init; }

    public AnalysisStatus Status { get; set; } = AnalysisStatus.Queued;

    public double? Bpm { get; set; }

    /// <summary>"F#m", "C" — display text.</summary>
    public string? Key { get; set; }

    public double? KeyConfidence { get; set; }

    public string? Error { get; set; }

    public required Instant CreatedAt { get; init; }

    public Instant? CompletedAt { get; set; }
}
