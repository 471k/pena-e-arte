namespace Pena_e_Arte.Domain.Interfaces;

public interface IR2Service
{
    Task<(string UploadUrl, string PublicUrl)> GeneratePresignedUploadUrlAsync(
        string objectKey, string contentType, CancellationToken ct);

    Task<string> GeneratePresignedReadUrlAsync(string fileUrl, CancellationToken ct);

    bool IsR2Url(string url);
}
