using NodaTime;

namespace Bandroom.Api.Data;

/// <summary>
/// Lyrics, chord sheets, charts — the pdfs and notes that belong to a song
/// (spec §8, phase 3 "files"). Same two-phase presigned upload as audio.
/// </summary>
public sealed class SongFile : IBandScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BandId { get; init; }

    public required Guid SongId { get; init; }

    public required string Title { get; set; }

    public required string FileKey { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public long SizeBytes { get; set; }

    public VersionStatus Status { get; set; } = VersionStatus.Uploading;

    public required Guid UploadedByMembershipId { get; init; }

    public required Instant CreatedAt { get; init; }
}
