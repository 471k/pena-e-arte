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

    public string? TryGetObjectKey(string storedUrl) => null;

    public IReadOnlyList<string> GetKnownPublicUrls(string objectKey)
        => throw new InvalidOperationException("Cloudflare R2 is not configured.");

    public Task<string> GeneratePresignedReadUrlAsync(string objectKey, TimeSpan ttl, CancellationToken ct)
        => throw new InvalidOperationException("Cloudflare R2 is not configured.");

    public string GetPublicUrl(string objectKey)
        => throw new InvalidOperationException("Cloudflare R2 is not configured.");

    public Task UploadAsync(string objectKey, byte[] data, string contentType, CancellationToken ct)
        => throw new InvalidOperationException("Cloudflare R2 is not configured.");

    // No-op: with no R2 configured there is no object to delete, and the retention job must
    // still be able to hard-purge the DB rows without crashing in an R2-less environment.
    public Task DeleteAsync(string objectKey, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<R2ObjectInfo>> ListByPrefixAsync(string prefix, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<R2ObjectInfo>>(Array.Empty<R2ObjectInfo>());

    public Task<IReadOnlyList<R2ObjectInfo>> ListAllExceptAsync(IReadOnlyCollection<string> skippedTopLevelFolders, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<R2ObjectInfo>>(Array.Empty<R2ObjectInfo>());

    // Unreachable in practice: the listing above is empty, so the sweep never has anything to copy.
    public Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct)
        => throw new InvalidOperationException("Cloudflare R2 is not configured.");

    public Task<long?> GetObjectSizeAsync(string objectKey, CancellationToken ct)
        => throw new InvalidOperationException("Cloudflare R2 is not configured.");
}
