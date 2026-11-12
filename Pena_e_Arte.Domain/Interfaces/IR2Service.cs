namespace Pena_e_Arte.Domain.Interfaces;

public record R2ObjectInfo(string Key, DateTime LastModified, long SizeBytes);

public interface IR2Service
{
    Task<(string UploadUrl, string PublicUrl)> GeneratePresignedUploadUrlAsync(
        string objectKey, string contentType, CancellationToken ct);

    Task<string> GeneratePresignedReadUrlAsync(string fileUrl, CancellationToken ct);

    Task<string> GeneratePresignedReadUrlAsync(string objectKey, TimeSpan ttl, CancellationToken ct);

    bool IsR2Url(string url);

    Task UploadAsync(string objectKey, byte[] data, string contentType, CancellationToken ct);

    /// <summary>Permanently deletes an object by key. Used by the retention hard-purge job.</summary>
    Task DeleteAsync(string objectKey, CancellationToken ct);

    string GetPublicUrl(string objectKey);

    /// <summary>
    /// Every URL a stored row may hold for this object: the current public address first, then
    /// any address the bucket was served from before. For matching stored URLs to objects.
    /// </summary>
    IReadOnlyList<string> GetKnownPublicUrls(string objectKey);

    Task<IReadOnlyList<R2ObjectInfo>> ListByPrefixAsync(string prefix, CancellationToken ct);
}
