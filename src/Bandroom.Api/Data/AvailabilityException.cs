using NodaTime;

namespace Bandroom.Api.Data;

/// <summary>
/// A dated deviation from the weekly pattern — "can't the 12th–15th". Inclusive
/// at both ends, mirroring NodaTime's DateInterval and the domain tests. Its own
/// table (not embedded) because the finder queries by date range (spec §7).
/// </summary>
public sealed class AvailabilityException : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BandId { get; init; }

    public required Guid MembershipId { get; init; }

    public required LocalDate From { get; init; }

    public required LocalDate To { get; init; }

    public string? Note { get; init; }

    public required Instant CreatedAt { get; init; }
}
