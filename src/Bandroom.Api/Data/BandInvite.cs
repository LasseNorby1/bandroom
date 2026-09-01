using NodaTime;

namespace Bandroom.Api.Data;

/// <summary>
/// A join link ("QR held up in the rehearsal room", spec §5.4). Same credential
/// discipline as refresh tokens: only the SHA-256 is stored, the raw token is
/// shown exactly once at creation.
/// </summary>
public sealed class BandInvite : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BandId { get; init; }

    public required string TokenHash { get; init; }

    public required Guid CreatedByMembershipId { get; init; }

    public required Instant CreatedAt { get; init; }

    public required Instant ExpiresAt { get; init; }

    /// <summary>Null = unlimited uses until expiry or revocation.</summary>
    public int? MaxUses { get; init; }

    public int UseCount { get; set; }

    public Instant? RevokedAt { get; set; }
}
