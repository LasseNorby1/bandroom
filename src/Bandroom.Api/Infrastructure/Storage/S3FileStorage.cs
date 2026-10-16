using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace Bandroom.Api.Infrastructure.Storage;

public sealed record StorageOptions
{
    /// <summary>The endpoint the api itself connects to.</summary>
    public string Endpoint { get; init; } = "";

    /// <summary>Endpoint baked into browser-facing presigned urls (defaults to Endpoint).</summary>
    public string? PublicEndpoint { get; init; }

    /// <summary>Endpoint baked into worker-facing presigned urls (defaults to PublicEndpoint/Endpoint).</summary>
    public string? WorkerEndpoint { get; init; }

    public string Bucket { get; init; } = "bandroom";

    public string AccessKey { get; init; } = "";

    public string SecretKey { get; init; } = "";

    /// <summary>Create the bucket on first use — dev/test convenience; off for r2.</summary>
    public bool EnsureBucket { get; init; }

    /// <summary>
    /// Browser origins allowed to PUT/GET presigned urls straight against the
    /// bucket. Applied as bucket cors together with EnsureBucket (self-hosted
    /// rustfs/minio); r2 configures cors in its own dashboard instead.
    /// </summary>
    public string[] AllowedOrigins { get; init; } = [];

    public int UploadUrlMinutes { get; init; } = 15;
}

public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly StorageOptions _options;
    private readonly AmazonS3Client _opsClient;
    private readonly Dictionary<string, AmazonS3Client> _signers = [];
    private readonly SemaphoreSlim _bucketGate = new(1, 1);
    private bool _bucketEnsured;

    public S3FileStorage(StorageOptions options)
    {
        _options = options;
        _opsClient = CreateClient(options.Endpoint);
        _signers[options.Endpoint] = _opsClient;
    }

    private AmazonS3Client CreateClient(string endpoint) =>
        new(
            new BasicAWSCredentials(_options.AccessKey, _options.SecretKey),
            new AmazonS3Config { ServiceURL = endpoint, ForcePathStyle = true });

    private string EndpointFor(StorageAudience audience) => audience switch
    {
        StorageAudience.Worker => _options.WorkerEndpoint ?? _options.PublicEndpoint ?? _options.Endpoint,
        _ => _options.PublicEndpoint ?? _options.Endpoint,
    };

    private AmazonS3Client SignerFor(StorageAudience audience)
    {
        var endpoint = EndpointFor(audience);
        lock (_signers)
        {
            if (!_signers.TryGetValue(endpoint, out var client))
            {
                client = CreateClient(endpoint);
                _signers[endpoint] = client;
            }

            return client;
        }
    }

    public async Task<PresignedUpload> CreateUploadAsync(
        string key, string contentType, CancellationToken ct, StorageAudience audience = StorageAudience.Browser)
    {
        await EnsureBucketAsync(ct);
        var expires = DateTimeOffset.UtcNow.AddMinutes(_options.UploadUrlMinutes);
        var url = await SignerFor(audience).GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = expires.UtcDateTime,
            ContentType = contentType,
        });
        return new PresignedUpload(MatchScheme(url, EndpointFor(audience)), key, expires);
    }

    public string GetDownloadUrl(string key, TimeSpan lifetime, StorageAudience audience = StorageAudience.Browser) =>
        MatchScheme(
            SignerFor(audience).GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = _options.Bucket,
                Key = key,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.Add(lifetime),
            }),
            EndpointFor(audience));

    public async Task<long?> GetSizeAsync(string key, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        try
        {
            var metadata = await _opsClient.GetObjectMetadataAsync(_options.Bucket, key, ct);
            return metadata.ContentLength;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        await _opsClient.DeleteObjectAsync(_options.Bucket, key, ct);
    }

    /// <summary>
    /// The sdk presigns as https regardless of the endpoint's scheme; dev minio is
    /// plain http. SigV4 does not sign the scheme, so rewriting it is safe.
    /// </summary>
    private static string MatchScheme(string url, string endpoint) =>
        endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? "http://" + url["https://".Length..]
            : url;

    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (!_options.EnsureBucket || _bucketEnsured)
        {
            return;
        }

        await _bucketGate.WaitAsync(ct);
        try
        {
            if (_bucketEnsured)
            {
                return;
            }

            try
            {
                await _opsClient.PutBucketAsync(_options.Bucket, ct);
            }
            catch (AmazonS3Exception ex) when (
                ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
            {
                // fine — someone got there first
            }

            if (_options.AllowedOrigins.Length > 0)
            {
                // Without this a browser's preflight for a presigned PUT gets no
                // Access-Control-* headers and every upload fails silently.
                await _opsClient.PutCORSConfigurationAsync(new PutCORSConfigurationRequest
                {
                    BucketName = _options.Bucket,
                    Configuration = new CORSConfiguration
                    {
                        Rules =
                        [
                            new CORSRule
                            {
                                AllowedOrigins = [.. _options.AllowedOrigins],
                                AllowedMethods = ["GET", "PUT", "HEAD"],
                                AllowedHeaders = ["*"],
                                ExposeHeaders = ["ETag"],
                                MaxAgeSeconds = 3600,
                            },
                        ],
                    },
                }, ct);
            }

            _bucketEnsured = true;
        }
        finally
        {
            _bucketGate.Release();
        }
    }

    public void Dispose()
    {
        lock (_signers)
        {
            foreach (var client in _signers.Values)
            {
                client.Dispose();
            }
        }

        _bucketGate.Dispose();
    }
}
