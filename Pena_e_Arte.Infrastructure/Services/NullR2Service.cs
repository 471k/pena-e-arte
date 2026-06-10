using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

internal sealed class NullR2Service : IR2Service
{
    public Task<(string UploadUrl, string PublicUrl)> GeneratePresignedUploadUrlAsync(
        string objectKey, string contentType, CancellationToken ct)
        => throw new InvalidOperationException("Cloudflare R2 is not configured.");

    public Task<string> GeneratePresignedReadUrlAsync(string fileUrl, CancellationToken ct)
        => throw new InvalidOperationException("Cloudflare R2 is not configured.");

    public bool IsR2Url(string url) => false;

    public Task<string> GeneratePresignedReadUrlAsync(string objectKey, TimeSpan ttl, CancellationToken ct)
        => throw new InvalidOperationException("Cloudflare R2 is not configured.");

    public Task UploadAsync(string objectKey, byte[] data, string contentType, CancellationToken ct)
        => throw new InvalidOperationException("Cloudflare R2 is not configured.");

    public Task<IReadOnlyList<R2ObjectInfo>> ListByPrefixAsync(string prefix, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<R2ObjectInfo>>(Array.Empty<R2ObjectInfo>());
}
