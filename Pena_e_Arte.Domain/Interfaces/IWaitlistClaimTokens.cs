using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// The waitlist claim link's credential (C09). A guest on the waitlist has no account, so the link in the
/// "a slot opened up" email is the only proof they received the offer. Implemented over ASP.NET Data
/// Protection with its own purpose string, on the same shared, DB-persisted key ring that signs Identity's
/// tokens; the purpose makes a claim token unusable as anything else, and anything else unusable as one.
///
/// The token carries no expiry of its own: the 24h window is read from the entry's NotifiedAt on every use,
/// so an expired link can be told apart from a forged one. It is bound to one notification by NotifiedAt's
/// ticks. It never authorises a booking; it only lets the claim page show the offer and lets a successful
/// booking mark that one entry Booked.
/// </summary>
public interface IWaitlistClaimTokens
{
    /// <summary>Mints the token for a Notified entry. Throws if the entry has no NotifiedAt.</summary>
    string Mint(Waitlist entry);

    /// <summary>False for anything that isn't a token this app minted for this purpose.</summary>
    bool TryRead(string? token, out WaitlistClaimRef claim);
}

/// <summary>What a verified claim token says. Trust it only after <see cref="IWaitlistClaimTokens.TryRead"/>.</summary>
public readonly record struct WaitlistClaimRef(Guid EntryId, Guid StudioId, long NotifiedAtTicks)
{
    /// <summary>True when the token belongs to this entry's current notification.</summary>
    public bool Matches(Waitlist entry) =>
        entry.Id == EntryId
        && entry.StudioId == StudioId
        && entry.NotifiedAt is DateTime notifiedAt
        && notifiedAt.Ticks == NotifiedAtTicks;
}
