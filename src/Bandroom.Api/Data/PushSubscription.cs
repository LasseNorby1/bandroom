using NodaTime;

namespace Bandroom.Api.Data;

/// <summary>
/// One browser's web-push subscription (spec principle 3: meet people where
/// they already look). Per user, not per band — a person gets one phone's
/// worth of notifications for all their bands. Not band-scoped on purpose.
/// </summary>
public sealed class PushSubscription
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    public required string Endpoint { get; init; }

    public required string P256dh { get; init; }

    public required string Auth { get; init; }

    public string? UserAgent { get; init; }

    public required Instant CreatedAt { get; init; }

    public Instant? LastUsedAt { get; set; }
}
