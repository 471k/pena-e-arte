using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.BookingDrafts.Queries;

/// <summary>The signed-in person's unexpired draft at the current studio, or null.</summary>
public record GetMyBookingDraftQuery : IRequest<BookingDraftResponse?>;

public class GetMyBookingDraftHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<GetMyBookingDraftQuery, BookingDraftResponse?>
{
    public async Task<BookingDraftResponse?> Handle(GetMyBookingDraftQuery query, CancellationToken ct)
    {
        DateTime now = time.GetUtcNow().UtcDateTime;
        // Tenant-scoped by the global query filter; the author check is UserId. An expired draft reads as none
        // even before BookingDraftExpiryJob removes it. ClaimedAt != null is what makes C05 safe: an unclaimed draft
        // was written by an anonymous caller who typed this person's email, and it stays invisible until the
        // resume link AND a sign-in prove it is theirs (ResumeBookingDraftCommand). Do not drop this predicate.
        BookingDraft? draft = await db.BookingDrafts
            .AsNoTracking()
            .Include(d => d.Attachments)
            .FirstOrDefaultAsync(d => d.UserId == currentUser.UserId && d.ExpiresAt > now && d.ClaimedAt != null, ct);

        return draft is null ? null : Map(draft);
    }

    internal static BookingDraftResponse Map(BookingDraft draft) => new(
        draft.PayloadJson,
        draft.Attachments
            .OrderBy(a => a.CreatedAt)
            .Select(a => new BookingDraftImageResponse(a.ImageUrl, a.Category.ToString()))
            .ToList(),
        draft.UpdatedAt,
        draft.ExpiresAt);
}
