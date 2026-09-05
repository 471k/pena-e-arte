using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetPresignedGuestUploadUrlQuery(string StudioSlug, PresignGuestUploadRequest Request)
    : IRequest<PresignUploadResponse>;

public class GetPresignedGuestUploadUrlHandler(IAppDbContext db, IR2Service r2)
    : IRequestHandler<GetPresignedGuestUploadUrlQuery, PresignUploadResponse>
{
    // Image types only — no application/pdf (Decision #10). A narrower subset of the existing
    // authenticated presign endpoint's ExtensionsByContentType, deliberately not shared with it —
    // that endpoint's latitude (PDF, free-ish prefix) must never reach an anonymous caller.
    private static readonly Dictionary<string, string> ExtensionsByContentType = new()
    {
        ["image/jpeg"] = "jpg",
        ["image/png"] = "png",
        ["image/webp"] = "webp",
    };

    public async Task<PresignUploadResponse> Handle(GetPresignedGuestUploadUrlQuery query, CancellationToken ct)
    {
        // Approved: public/anonymous studio-slug resolution — shared via PublicStudioLookupExtensions.
        // Deliberately leaks no more than that handler already does (404 either way).
        Studio studio = await db.GetPublishedStudioBySlugAsync(query.StudioSlug, ct)
            ?? throw new NotFoundException(nameof(Studio), query.StudioSlug);

        // Category is normalized to lowercase "area"/"reference" by the request; the R2 key uses
        // that lowercase form directly (validator enforces the closed set).
        string category = query.Request.Category;
        string extension = ExtensionsByContentType[query.Request.ContentType];

        // Entire key is server-constructed — the request carries no key/prefix material at all,
        // unlike the existing authenticated presign endpoint's "trust the folder prefix" split
        // (Decision #10). GuestPendingUploadCleanupJob sweeps this exact prefix for orphans.
        string objectKey = $"appointments/guest-pending/{studio.Id}/{category}/{Guid.NewGuid():N}.{extension}";

        (string uploadUrl, string publicUrl) =
            await r2.GeneratePresignedUploadUrlAsync(objectKey, query.Request.ContentType, ct);
        return new PresignUploadResponse(uploadUrl, publicUrl);
    }
}
