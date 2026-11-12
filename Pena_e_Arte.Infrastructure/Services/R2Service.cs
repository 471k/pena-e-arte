using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

public class R2Service(IAmazonS3 s3, IOptions<R2Options> options) : IR2Service
{
    private readonly R2Options _opts = options.Value;

    public Task<(string UploadUrl, string PublicUrl)> GeneratePresignedUploadUrlAsync(
        string objectKey, string contentType, CancellationToken ct)
    {
        GetPreSignedUrlRequest request = new()
        {
            BucketName = _opts.BucketName,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = DateTime.UtcNow.AddMinutes(15)
        };

        string uploadUrl = s3.GetPreSignedURL(request);
        string publicUrl = $"{_opts.PublicUrl.TrimEnd('/')}/{objectKey}";

        return Task.FromResult((uploadUrl, publicUrl));
    }

    /// <summary>
    /// Each address this bucket is or was served from, as "https://host/" (trailing slash, so
    /// "https://host.evil.com/..." can't pass for "https://host/..."). Current address first.
    /// </summary>
    private IEnumerable<string> KnownPrefixes() =>
        new[] { _opts.PublicUrl }
            .Concat(_opts.LegacyPublicUrls)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim().TrimEnd('/') + "/")
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>The object key behind a stored URL; the value itself when no known address matches.</summary>
    private string ToObjectKey(string urlOrKey)
    {
        string? prefix = KnownPrefixes()
            .FirstOrDefault(p => urlOrKey.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        return prefix is null ? urlOrKey : urlOrKey[prefix.Length..];
    }

    public Task<string> GeneratePresignedReadUrlAsync(string fileUrl, CancellationToken ct)
    {
        string objectKey = ToObjectKey(fileUrl);

        GetPreSignedUrlRequest request = new()
        {
            BucketName = _opts.BucketName,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(15)
        };

        return Task.FromResult(s3.GetPreSignedURL(request));
    }

    public bool IsR2Url(string url) =>
        !string.IsNullOrEmpty(url) &&
        KnownPrefixes().Any(p => url.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    public Task<string> GeneratePresignedReadUrlAsync(string objectKey, TimeSpan ttl, CancellationToken ct)
    {
        string key = ToObjectKey(objectKey);

        GetPreSignedUrlRequest request = new()
        {
            BucketName = _opts.BucketName,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(ttl)
        };

        return Task.FromResult(s3.GetPreSignedURL(request));
    }

    public async Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        await s3.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _opts.BucketName,
            Key = objectKey,
        }, ct);
    }

    public string GetPublicUrl(string objectKey) =>
        $"{_opts.PublicUrl.TrimEnd('/')}/{objectKey}";

    public IReadOnlyList<string> GetKnownPublicUrls(string objectKey) =>
        KnownPrefixes().Select(p => p + objectKey).ToList();

    public async Task UploadAsync(string objectKey, byte[] data, string contentType, CancellationToken ct)
    {
        using MemoryStream stream = new(data);
        PutObjectRequest request = new()
        {
            BucketName = _opts.BucketName,
            Key = objectKey,
            InputStream = stream,
            ContentType = contentType,
            // R2 rejects aws-chunked streaming signatures ("STREAMING-AWS4-HMAC-SHA256-PAYLOAD not implemented");
            // UNSIGNED-PAYLOAD is allowed because the endpoint is HTTPS.
            DisablePayloadSigning = true,
        };
        await s3.PutObjectAsync(request, ct);
    }

    public async Task<IReadOnlyList<R2ObjectInfo>> ListByPrefixAsync(string prefix, CancellationToken ct)
    {
        ListObjectsV2Request request = new()
        {
            BucketName = _opts.BucketName,
            Prefix = prefix,
        };

        ListObjectsV2Response response = await s3.ListObjectsV2Async(request, ct);

        return (response.S3Objects ?? [])
            .Select(o => new R2ObjectInfo(
                o.Key,
                o.LastModified ?? DateTime.UtcNow,
                o.Size ?? 0L))
            .ToList();
    }
}
