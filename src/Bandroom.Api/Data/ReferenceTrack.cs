using NodaTime;

namespace Bandroom.Api.Data;

/// <summary>
/// The band's reference library — "make it sit like this song". Uploaded once,
/// reusable across ideas; ai polish matches the mix against it (spec §5.5).
/// </summary>
public sealed class ReferenceTrack : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BandId { get; init; }

    public required string Title { get; set; }

    public required string FileKey { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public long SizeBytes { get; set; }

    public VersionStatus Status { get; set; } = VersionStatus.Uploading;

    public required Guid UploadedByMembershipId { get; init; }

    public required Instant CreatedAt { get; init; }
}
