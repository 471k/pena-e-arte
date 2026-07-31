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

    public Task<string> GeneratePresignedReadUrlAsync(string fileUrl, CancellationToken ct)
    {
        string prefix = _opts.PublicUrl.TrimEnd('/') + "/";
        string objectKey = fileUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? fileUrl[prefix.Length..]
            : fileUrl;

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
        url.StartsWith(_opts.PublicUrl, StringComparison.OrdinalIgnoreCase);

    public Task<string> GeneratePresignedReadUrlAsync(string objectKey, TimeSpan ttl, CancellationToken ct)
    {
        string prefix = _opts.PublicUrl.TrimEnd('/') + "/";
        string key = objectKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? objectKey[prefix.Length..]
            : objectKey;

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

    public async Task UploadAsync(string objectKey, byte[] data, string contentType, CancellationToken ct)
    {
        using MemoryStream stream = new(data);
        PutObjectRequest request = new()
        {
            BucketName = _opts.BucketName,
            Key = objectKey,
            InputStream = stream,
            ContentType = contentType,
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
