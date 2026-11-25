using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.BookingDrafts.Queries;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.BookingDrafts.Commands;

/// <summary>
/// C05: attaches an UNCLAIMED draft (saved by the guest-booking collision path) to the signed-in caller. Both proofs
/// are required, and that is the point: the token proves control of the mailbox the email went to, the sign-in
/// proves control of the account. Neither alone attaches anything.
/// </summary>
public record ResumeBookingDraftCommand(string Token) : IRequest<ResumeBookingDraftResponse>;

public class ResumeBookingDraftHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IBookingDraftResumeTokens tokens,
    TimeProvider time)
    : IRequestHandler<ResumeBookingDraftCommand, ResumeBookingDraftResponse>
{
    public const string Resumed = "Resumed";
    public const string AlreadyClaimed = "AlreadyClaimed";
    public const string Expired = "Expired";

    public async Task<ResumeBookingDraftResponse> Handle(ResumeBookingDraftCommand command, CancellationToken ct)
    {
        // A token this app did not mint for this purpose.
        if (!tokens.TryRead(command.Token, out BookingDraftResumeRef resume)) throw NotFound();

        DateTime now = time.GetUtcNow().UtcDateTime;

        // Expiry is read from the TOKEN, before any row is looked at, so saying "expired" reveals nothing about the
        // draft, the account, or whether the caller owns it: only the link's own age, which its holder already knows
        // from the email's date. This is the reason the token is not a time-limited protector. The row may already
        // be gone (BookingDraftExpiryJob), which is exactly when an "expired" message is still owed.
        if (resume.ExpiresAt <= now) return new ResumeBookingDraftResponse(Expired, null);

        // Tenant-scoped by the global query filter: the caller must be signed in to the draft's studio (the page
        // switches studio first). Every row-dependent failure below is the SAME not-found, so the endpoint never says
        // which of them happened.
        BookingDraft? draft = await db.BookingDrafts
            .Include(d => d.Attachments)
            .FirstOrDefaultAsync(d => d.Id == resume.DraftId, ct);
        if (draft is null
            || !resume.Matches(draft)
            || draft.UserId != currentUser.UserId
            || draft.ExpiresAt <= now)
        {
            throw NotFound();
        }

        if (draft.ClaimedAt is not null)
            return new ResumeBookingDraftResponse(AlreadyClaimed, GetMyBookingDraftHandler.Map(draft));

        draft.ClaimedAt = now;
        await db.SaveChangesAsync(ct);
        return new ResumeBookingDraftResponse(Resumed, GetMyBookingDraftHandler.Map(draft));
    }

    /// <summary>The one not-found, identical for a forged token, a draft that is gone, one past its time, and one
    /// that belongs to someone else. Carries no id.</summary>
    private static NotFoundException NotFound() => new("Booking draft", "from this link");
}

public class ResumeBookingDraftValidator : AbstractValidator<ResumeBookingDraftCommand>
{
    public ResumeBookingDraftValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(2048);
    }
}
