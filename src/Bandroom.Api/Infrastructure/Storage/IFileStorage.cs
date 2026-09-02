namespace Bandroom.Api.Infrastructure.Storage;

public sealed record PresignedUpload(string Url, string Key, DateTimeOffset ExpiresAt);

/// <summary>
/// SigV4 signs the host, so a presigned url is only valid for the endpoint it
/// was signed against — and in dev the browser (localhost), the api (localhost)
/// and the worker container (compose dns) see storage under different names.
/// Urls are therefore signed per audience; in production every audience uses the
/// same public r2 endpoint and the extra options stay unset.
/// </summary>
public enum StorageAudience
{
    Browser = 0,
    Worker = 1,
}

/// <summary>
/// S3-compatible object storage (minio in dev, r2 in prod). Audio bytes move
/// browser↔storage via presigned urls — never through the api (spec §6).
/// </summary>
public interface IFileStorage
{
    Task<PresignedUpload> CreateUploadAsync(
        string key, string contentType, CancellationToken ct, StorageAudience audience = StorageAudience.Browser);

    string GetDownloadUrl(string key, TimeSpan lifetime, StorageAudience audience = StorageAudience.Browser);

    Task<long?> GetSizeAsync(string key, CancellationToken ct);

    Task DeleteAsync(string key, CancellationToken ct);
}
