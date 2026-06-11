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

    Task<IReadOnlyList<R2ObjectInfo>> ListByPrefixAsync(string prefix, CancellationToken ct);
}
