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

    Task<IReadOnlyList<R2ObjectInfo>> ListByPrefixAsync(string prefix, CancellationToken ct);
}
