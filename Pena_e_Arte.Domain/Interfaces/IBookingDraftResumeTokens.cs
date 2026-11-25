using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// C05: the token in a "booking attempt" email's resume link. A guest booking made with an email that already has
/// an account saves an UNCLAIMED draft for that account; the token proves control of the mailbox, signing in proves
/// control of the account, and only both together attach the draft (ResumeBookingDraftCommand). Copies the
/// IWaitlistClaimTokens pattern on purpose rather than generalising it.
/// </summary>
public interface IBookingDraftResumeTokens
{
    /// <summary>Mints the token for a saved draft. The draft's CreatedAt must be whole seconds (it is stored
    /// and compared as ticks, and the database keeps microseconds at most).</summary>
    string Mint(BookingDraft draft);

    /// <summary>False for anything that isn't a token this app minted for this purpose.</summary>
    bool TryRead(string? token, out BookingDraftResumeRef resume);
}

/// <summary>What a verified resume token says. Trust it only after <see cref="IBookingDraftResumeTokens.TryRead"/>.</summary>
public readonly record struct BookingDraftResumeRef(Guid DraftId, Guid StudioId, long CreatedAtTicks)
{
    /// <summary>When the draft the token was minted for stops being resumable. Read from the token alone, so an
    /// expired link can be told apart from a forged one without looking at any row: the same reason the token is
    /// not a time-limited protector. Unclaimed drafts are never extended, so this equals the row's ExpiresAt.</summary>
    public DateTime ExpiresAt => new DateTime(CreatedAtTicks, DateTimeKind.Utc) + BookingDraft.Lifetime;

    /// <summary>True when the token was minted for this draft, at this studio, in this write.</summary>
    public bool Matches(BookingDraft draft) =>
        draft.Id == DraftId
        && draft.StudioId == StudioId
        && draft.CreatedAt.Ticks == CreatedAtTicks;
}
