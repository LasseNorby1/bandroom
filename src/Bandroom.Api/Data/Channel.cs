using NodaTime;

namespace Bandroom.Api.Data;

/// <summary>Chat rooms (spec §5.3) — every band gets #general; more on demand.</summary>
public sealed class Channel : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BandId { get; init; }

    public required string Name { get; set; }

    public bool IsDefault { get; init; }

    public required Instant CreatedAt { get; init; }
}

public sealed class Message : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BandId { get; init; }

    public required Guid ChannelId { get; init; }

    public required Guid AuthorMembershipId { get; init; }

    public required string Body { get; init; }

    public required Instant CreatedAt { get; init; }
}
