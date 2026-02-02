namespace MineralKingdom.Infrastructure.Media.Storage;

public sealed record SignedPutResult(
  Uri UploadUrl,
  DateTimeOffset ExpiresAt,
  IReadOnlyDictionary<string, string> RequiredHeaders
);

public interface IObjectStorage
{
  Task<SignedPutResult> CreateSignedPutAsync(
    string bucket,
    string key,
    string contentType,
    long contentLengthBytes,
    TimeSpan expiresIn,
    CancellationToken ct
  );

  Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct);
}
