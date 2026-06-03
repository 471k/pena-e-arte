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
            BucketName  = _opts.BucketName,
            Key         = objectKey,
            Verb        = HttpVerb.PUT,
            ContentType = contentType,
            Expires     = DateTime.UtcNow.AddMinutes(15)
        };

        string uploadUrl = s3.GetPreSignedURL(request);
        string publicUrl = $"{_opts.PublicUrl.TrimEnd('/')}/{objectKey}";

        return Task.FromResult((uploadUrl, publicUrl));
    }

    public bool IsR2Url(string url) =>
        !string.IsNullOrEmpty(url) &&
        url.StartsWith(_opts.PublicUrl, StringComparison.OrdinalIgnoreCase);
}
