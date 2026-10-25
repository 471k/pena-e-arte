namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A client's card on file, tied to one studio's POK merchant account — a token saved at one
/// studio is not usable at another (ADR-0001: each studio has its own POK merchant, there is no
/// platform-level account). Holds only what's safe to display and reuse: POK's own card token id
/// plus thin, optional display metadata. NEVER a raw PAN, CVV, or the JWE AddCardForm produces —
/// those are exchanged for the token server-side (PokCardTokenService.TokenizeCardAsync) and
/// never persisted anywhere (PCI SAQ-A).
/// </summary>
public class SavedPaymentMethod : TenantEntity
{
    public Guid ClientId { get; set; }

    /// <summary>Future-proofing only — POK is the sole provider today.</summary>
    public string Provider { get; set; } = "pok";

    /// <summary>POK's own tokenized-card id (credit-debit-cards resource id).</summary>
    public string ProviderCardTokenId { get; set; } = string.Empty;

    /// <summary>Display only, from POK's card information ("Visa", "**** 1234"). Null when POK
    /// doesn't return them; display degrades to a generic "Card on file" label rather than
    /// blocking the save. POK keeps no expiry date, so ExpiryMonth/ExpiryYear stay null for new
    /// cards.</summary>
    public string? CardBrand { get; set; }
    public string? MaskedPan { get; set; }
    public string? ExpiryMonth { get; set; }
    public string? ExpiryYear { get; set; }

    public bool IsDefault { get; set; }

    public Client Client { get; set; } = null!;
}
