namespace MineralKingdom.Infrastructure.Media.Storage;

public sealed class FakeObjectStorage : IObjectStorage
{
  public Task<SignedPutResult> CreateSignedPutAsync(
    string bucket,
    string key,
    string contentType,
    long contentLengthBytes,
    TimeSpan expiresIn,
    CancellationToken ct)
  {
    var expiresAt = DateTimeOffset.UtcNow.Add(expiresIn);
    var url = new Uri($"http://fake-upload.local/{bucket}/{Uri.EscapeDataString(key)}");

    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      ["Content-Type"] = contentType
    };

    return Task.FromResult(new SignedPutResult(url, expiresAt, headers));
  }

  public Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct)
    => Task.FromResult(true);
}
