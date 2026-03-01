using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using MineralKingdom.Infrastructure.Configuration;

namespace MineralKingdom.Infrastructure.Media.Storage;

public sealed class S3ObjectStorage : IObjectStorage
{
  private readonly AmazonS3Client _client;

  public S3ObjectStorage(MediaStorageOptions opts)
  {
    if (string.IsNullOrWhiteSpace(opts.AccessKey) ||
        string.IsNullOrWhiteSpace(opts.SecretKey))
      throw new InvalidOperationException("MK_MEDIA AccessKey/SecretKey are required for S3 provider.");

    var creds = new BasicAWSCredentials(opts.AccessKey, opts.SecretKey);

    var cfg = new AmazonS3Config();

    // S3-compatible (R2/MinIO/Spaces): ServiceUrl + ForcePathStyle
    if (!string.IsNullOrWhiteSpace(opts.ServiceUrl))
    {
      cfg.ServiceURL = opts.ServiceUrl;
      cfg.ForcePathStyle = true;

      // R2 requires SigV4; SDK uses this region for signing
      cfg.AuthenticationRegion = string.IsNullOrWhiteSpace(opts.Region) ? "us-east-1" : opts.Region;

      // IMPORTANT: do NOT set RegionEndpoint when using ServiceURL,
      // or the SDK may route/presign against AWS endpoints.
    }
    else
    {
      // Real AWS S3
      if (!string.IsNullOrWhiteSpace(opts.Region))
        cfg.RegionEndpoint = RegionEndpoint.GetBySystemName(opts.Region);
    }

    _client = new AmazonS3Client(creds, cfg);
  }

  public Task<SignedPutResult> CreateSignedPutAsync(
    string bucket,
    string key,
    string contentType,
    long contentLengthBytes,
    TimeSpan expiresIn,
    CancellationToken ct)
  {
    var expiresAt = DateTimeOffset.UtcNow.Add(expiresIn);

    var req = new GetPreSignedUrlRequest
    {
      BucketName = bucket,
      Key = key,
      Verb = HttpVerb.PUT,
      Expires = expiresAt.UtcDateTime,
      ContentType = contentType
    };

    var url = _client.GetPreSignedURL(req);

    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      ["Content-Type"] = contentType
    };

    return Task.FromResult(new SignedPutResult(new Uri(url), expiresAt, headers));
  }

  public async Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct)
  {
    try
    {
      var req = new GetObjectMetadataRequest { BucketName = bucket, Key = key };
      await _client.GetObjectMetadataAsync(req, ct);
      return true;
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
      return false;
    }
  }

  public async Task DeleteAsync(string bucket, string key, CancellationToken ct)
  {
    var req = new DeleteObjectRequest
    {
      BucketName = bucket,
      Key = key
    };

    await _client.DeleteObjectAsync(req, ct);
  }
}
