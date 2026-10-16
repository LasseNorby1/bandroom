using NodaTime;

namespace Bandroom.Api.Data;

/// <summary>
/// 🔥 on a demo version — "and little else" (spec §5.2). One per member per
/// version; the emoji is implied, so there is nothing to pick.
/// </summary>
public sealed class Reaction : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BandId { get; init; }

    public required Guid VersionId { get; init; }

    public required Guid MembershipId { get; init; }

    public required Instant CreatedAt { get; init; }
}
