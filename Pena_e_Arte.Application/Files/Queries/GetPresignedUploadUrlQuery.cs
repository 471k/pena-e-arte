using MediatR;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Files.Queries;

public record GetPresignedUploadUrlQuery(PresignUploadRequest Request) : IRequest<PresignUploadResponse>;

public class GetPresignedUploadUrlHandler(IR2Service r2, ICurrentTenant tenant)
    : IRequestHandler<GetPresignedUploadUrlQuery, PresignUploadResponse>
{
    // Content types allowed by GetPresignedUploadUrlValidator — the file name extension is
    // derived from the validated ContentType, never trusted from a client-supplied file name.
    private static readonly Dictionary<string, string> ExtensionsByContentType = new()
    {
        ["image/jpeg"] = "jpg",
        ["image/png"] = "png",
        ["image/webp"] = "webp",
        ["application/pdf"] = "pdf",
    };

    public async Task<PresignUploadResponse> Handle(
        GetPresignedUploadUrlQuery query, CancellationToken ct)
    {
        string folderPrefix = GetFolderPrefix(query.Request.ObjectKey);
        string extension = ExtensionsByContentType[query.Request.ContentType];
        string fileName = $"{Guid.NewGuid():N}.{extension}";
        string scopedKey = string.IsNullOrEmpty(folderPrefix)
            ? $"{tenant.StudioId}/{fileName}"
            : $"{tenant.StudioId}/{folderPrefix}/{fileName}";

        (string uploadUrl, string publicUrl) =
            await r2.GeneratePresignedUploadUrlAsync(scopedKey, query.Request.ContentType, ct);
        return new PresignUploadResponse(uploadUrl, publicUrl);
    }

    // Only the client-supplied folder/purpose prefix (e.g. "designs/{designId}") is kept — the
    // file name itself is always server-generated, closing the same-tenant overwrite/collision
    // risk a fully client-chosen key allowed (Finding 6).
    private static string GetFolderPrefix(string objectKey)
    {
        string trimmed = objectKey.Trim('/');
        int lastSlash = trimmed.LastIndexOf('/');
        return lastSlash < 0 ? string.Empty : trimmed[..lastSlash];
    }
}
