using NodaTime;

namespace Bandroom.Api.Data;

/// <summary>
/// Only the SHA-256 of the token is stored — a database leak must not leak usable
/// credentials. FamilyId ties rotations together: presenting an already-used or
/// revoked token is treated as theft and revokes the entire family.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    public required string TokenHash { get; init; }

    public required Guid FamilyId { get; init; }

    public required Instant CreatedAt { get; init; }

    public required Instant ExpiresAt { get; init; }

    public Instant? UsedAt { get; set; }

    public Instant? RevokedAt { get; set; }
}
