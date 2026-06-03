using MediatR;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Files.Queries;

public record GetPresignedUploadUrlQuery(PresignUploadRequest Request) : IRequest<PresignUploadResponse>;

public class GetPresignedUploadUrlHandler(IR2Service r2, ICurrentTenant tenant)
    : IRequestHandler<GetPresignedUploadUrlQuery, PresignUploadResponse>
{
    public async Task<PresignUploadResponse> Handle(
        GetPresignedUploadUrlQuery query, CancellationToken ct)
    {
        string scopedKey = $"{tenant.StudioId}/{query.Request.ObjectKey.TrimStart('/')}";
        (string uploadUrl, string publicUrl) =
            await r2.GeneratePresignedUploadUrlAsync(scopedKey, query.Request.ContentType, ct);
        return new PresignUploadResponse(uploadUrl, publicUrl);
    }
}
