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

    public string? TryGetObjectKey(string storedUrl)
    {
        if (string.IsNullOrEmpty(storedUrl)) return null;
        string? prefix = KnownPrefixes()
            .FirstOrDefault(p => storedUrl.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        return prefix is null || storedUrl.Length == prefix.Length ? null : storedUrl[prefix.Length..];
    }

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
        // ListObjectsV2 returns at most 1,000 keys per call. Until 17 Nov 2026 only the first page was read,
        // so a studio with more than 1,000 objects was under-counted by StorageReconciliationJob, and a
        // whole-bucket listing (OrphanedObjectSweepJob) would have stopped at 1,000. Follow the token.
        List<R2ObjectInfo> objects = [];
        string? continuationToken = null;
        do
        {
            ListObjectsV2Request request = new()
            {
                BucketName = _opts.BucketName,
                Prefix = prefix,
                ContinuationToken = continuationToken,
            };

            ListObjectsV2Response response = await s3.ListObjectsV2Async(request, ct);

            objects.AddRange((response.S3Objects ?? [])
                .Select(o => new R2ObjectInfo(
                    o.Key,
                    ToUtc(o.LastModified),
                    o.Size ?? 0L,
                    o.ETag)));

            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
        }
        while (!string.IsNullOrEmpty(continuationToken));

        return objects;
    }

    /// <summary>
    /// S3 has no "list everything except" call, so this lists the top level with Delimiter "/" (loose root
    /// objects plus one CommonPrefix per top-level folder) and then lists each folder that is not skipped.
    /// A skipped folder's objects are never requested. Only top-level folders can be skipped: a deeper prefix
    /// would need its parent listed, which is the cost this method exists to avoid.
    /// </summary>
    public async Task<IReadOnlyList<R2ObjectInfo>> ListAllExceptAsync(
        IReadOnlyCollection<string> skippedTopLevelFolders, CancellationToken ct)
    {
        foreach (string folder in skippedTopLevelFolders)
        {
            if (folder.Length < 2 || folder.IndexOf('/') != folder.Length - 1)
                throw new ArgumentException($"'{folder}' is not a top-level folder (expected 'name/').", nameof(skippedTopLevelFolders));
        }

        List<R2ObjectInfo> objects = [];
        List<string> folders = [];
        string? continuationToken = null;
        do
        {
            ListObjectsV2Response response = await s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _opts.BucketName,
                Delimiter = "/",
                ContinuationToken = continuationToken,
            }, ct);

            objects.AddRange((response.S3Objects ?? [])
                .Select(o => new R2ObjectInfo(o.Key, ToUtc(o.LastModified), o.Size ?? 0L, o.ETag)));
            folders.AddRange(response.CommonPrefixes ?? []);

            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
        }
        while (!string.IsNullOrEmpty(continuationToken));

        foreach (string folder in folders.Where(f => !skippedTopLevelFolders.Contains(f, StringComparer.Ordinal)))
            objects.AddRange(await ListByPrefixAsync(folder, ct));

        return objects;
    }

    public async Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct)
    {
        await s3.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = _opts.BucketName,
            SourceKey = sourceKey,
            DestinationBucket = _opts.BucketName,
            DestinationKey = destinationKey,
        }, ct);
    }

    public async Task<R2ObjectStat?> HeadAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            GetObjectMetadataResponse response = await s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _opts.BucketName,
                Key = objectKey,
            }, ct);
            return new R2ObjectStat(response.ContentLength, response.ETag);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Every LastModified leaves here as DateTimeKind.Utc. Callers compare it with a UTC cutoff, and DateTime
    /// comparison ignores Kind, so a Local or Unspecified value would be correct only while the pod's clock
    /// happens to be UTC. OrphanedObjectSweepJob's minimum age depends on it, and the unsafe direction (an
    /// object looking older than it is) moves live files into the delete set. S3 sends UTC on the wire, so an
    /// Unspecified value is LABELLED Utc, not converted; a Local one is converted. A missing timestamp reads
    /// as "now": brand new, so never old enough to sweep.
    /// </summary>
    internal static DateTime ToUtc(DateTime? lastModified) => lastModified switch
    {
        null => DateTime.UtcNow,
        { Kind: DateTimeKind.Unspecified } lm => DateTime.SpecifyKind(lm, DateTimeKind.Utc),
        { } lm => lm.ToUniversalTime(),
    };
}
