using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Common;

public static class PublicStudioLookupExtensions
{
    /// <summary>
    /// The "resolve a studio for an anonymous/public caller" predicate — Slug match, IsActive,
    /// IsPublished — shared by every public/guest handler that needs it. Was copy-pasted
    /// identically across 6 handlers (GetPublicStudioQuery and the 5 guest-checkout-booking
    /// handlers), each individually commented "same predicate as GetPublicStudioHandler" rather
    /// than reusing one implementation. Found via /code-review, 2026-09-01.
    /// Returns null on no match — callers that need a 404 instead do
    /// `?? throw new NotFoundException(...)` at the call site, same as before this extraction.
    /// Approved IgnoreQueryFilters() usage — see architecture.md's AllowAnonymous Exceptions /
    /// IgnoreQueryFilters tables; unchanged by this extraction, just centralized.
    /// </summary>
    public static Task<Studio?> GetPublishedStudioBySlugAsync(
        this IAppDbContext db, string slug, CancellationToken ct) =>
        db.Studios
          .IgnoreQueryFilters()
          .FirstOrDefaultAsync(s => s.Slug == slug && s.IsActive && s.IsPublished, ct);
}
