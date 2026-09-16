namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// POK's card-on-file tokenization + pay-by-token 3DS setup, for the client-facing Saved
/// Payment Method feature. Deliberately NOT a member of IPaymentProvider — tokenization has no
/// Stripe/other-provider equivalent to stay neutral against, and IPaymentProvider is meant to
/// stay a provider-neutral Flow A abstraction (hold/capture/cancel/status/refund only).
///
/// <b>Wire shapes here are the least-verified code in this codebase</b> — more so even than
/// PokPaymentProvider's own flagged AmountInCentsToPok/MapStatus assumptions. POK's public docs
/// (docs/payments/pok-assessment.md) never give a full request/response schema for the
/// "Tokenize Card API" — only that it exists and exchanges AddCardForm's csFlexCard.jwe payload
/// for a permanent cardId. What IS confirmed, verbatim, from POK's docs:
/// - AddCardForm's onSuccess payload is `{ csFlexCard: { jwe }, billingInfo: {...} }`.
/// - `POST https://api.pokpay.io/credit-debit-cards/{cardId}/setup-tokenized-3ds` (root-level,
///   NOT under /merchants/{merchantId}/ like sdk-orders) returns data containing
///   `payerAuthSetupReferenceId` — confirmed field name, quoted directly from POK's Flutter SDK
///   docs.
/// - "usePOK charges a previously tokenized card... your backend must create a new SDK order"
///   — so SetupTokenizedThreeDsAsync's caller must already have a fresh order (the exact same
///   IPaymentProvider.CreatePaymentHoldAsync/sdk-orders flow used for a new-card checkout).
/// Everything else — the exact tokenize-card endpoint path/request/response field names beyond
/// cardId, and setup-tokenized-3ds's exact request body — is inferred from REST convention, not
/// confirmed. Verify all of it against a real POK sandbox transaction before trusting this for
/// production card traffic (same posture, and same user-approved "build now, verify later"
/// tradeoff, as PokPaymentProvider's own unverified assumptions).
/// </summary>
public interface IPokCardTokenService
{
    /// <summary>Exchanges a single-use JWE (from AddCardForm.onSuccess) for a permanent card
    /// token. The JWE and securityCode must never be persisted or logged (PCI SAQ-A) — only
    /// this method's return value.</summary>
    Task<PokTokenizedCard> TokenizeCardAsync(
        Guid studioId, string jwe, string? securityCode, PokCardBillingInfo billingInfo, CancellationToken ct);

    /// <summary>Sets up the 3DS challenge for charging a previously tokenized card against a
    /// freshly created SDK order (orderId — create it via IPaymentProvider.CreatePaymentHoldAsync
    /// first, same as any new-card checkout). Returns exactly what the frontend's POK SDK
    /// `PayerAuthentication` object needs to call `payByCardToken`.</summary>
    Task<PokPayerAuthSetup> SetupTokenizedThreeDsAsync(Guid studioId, string orderId, string cardTokenId, CancellationToken ct);
}

/// <summary>Billing details AddCardForm collects alongside the encrypted card payload (the
/// widget's own AddCardData.billingInfo shape — every field it returns, forwarded as-is) —
/// passed through to POK's tokenize-card call, never persisted locally beyond what
/// TokenizeCardAsync's own PokTokenizedCard result carries back.</summary>
public sealed record PokCardBillingInfo(
    string FirstName, string LastName, string Email, string CountryCode,
    string? AdministrativeArea, string? Locality, string? Address1,
    string? PostalCode, string? PhoneNumber);

/// <summary>Card metadata safe to persist and display — never a PAN, never the JWE. Brand/
/// MaskedPan/ExpiryMonth/ExpiryYear are null if POK's tokenize-card response doesn't return them
/// (unconfirmed — see IPokCardTokenService's own doc comment); a SavedPaymentMethod must degrade
/// to those being null rather than block the save.</summary>
public sealed record PokTokenizedCard(
    string CardTokenId, string? Brand, string? MaskedPan, string? ExpiryMonth, string? ExpiryYear);

/// <summary>Maps 1:1 onto the frontend POK SDK's own `PayerAuthentication` TypeScript type —
/// field names are intentionally identical so the API response can be passed through with no
/// remapping.</summary>
public sealed record PokPayerAuthSetup(
    string PayerAuthSetupReferenceId, PokDeviceDataCollection? DeviceDataCollection);

public sealed record PokDeviceDataCollection(string Url, string AccessToken);
