using NodaTime;

namespace Bandroom.Api.Data;

public enum BandRole
{
    Member = 0,
    Admin = 1,
}

/// <summary>
/// The workhorse join (spec §7): you-in-this-band. Roles live here, not on the
/// user — and in step 4 the weekly availability pattern joins them, because
/// availability is also a property of you-in-this-band.
/// </summary>
public sealed class Membership : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BandId { get; init; }

    public required Guid UserId { get; init; }

    public required BandRole Role { get; set; }

    public string? Instrument { get; set; }

    public required Instant JoinedAt { get; init; }
}
