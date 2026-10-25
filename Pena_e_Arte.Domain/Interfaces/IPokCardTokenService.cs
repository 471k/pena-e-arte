namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// POK's card-on-file tokenization + pay-by-token 3DS setup, for the client-facing Saved
/// Payment Method feature. Deliberately NOT a member of IPaymentProvider — tokenization has no
/// Stripe/other-provider equivalent to stay neutral against, and IPaymentProvider is meant to
/// stay a provider-neutral Flow A abstraction (hold/capture/cancel/status/refund only).
///
/// Endpoints, from POK's documented Card Tokenization API (C-9):
/// - `POST /credit-debit-cards/tokenize-guest-card` exchanges AddCardForm's
///   `{ csFlexCard: { jwe }, billingInfo, securityCode }` for `creditDebitCard { id, hiddenNumber, ... }`.
/// - `POST /credit-debit-cards/get-guest-cards-information` returns the card's `brand` and masked
///   `number` (display only).
/// - `POST /credit-debit-cards/{id}/setup-tokenized-3ds` with `{ sdkOrder: { id } }` returns the
///   `payerAuthentication` the frontend SDK's `payByCardToken` needs. The caller must already have
///   a fresh order (IPaymentProvider.CreatePaymentHoldAsync, same as a new-card checkout).
/// All three are root-level, not under /merchants/{merchantId}/ like sdk-orders.
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
/// passed through to POK's tokenize-card call, never persisted locally.</summary>
public sealed record PokCardBillingInfo(
    string FirstName, string LastName, string Email, string CountryCode,
    string? AdministrativeArea, string? Locality, string? Address1,
    string? PostalCode, string? PhoneNumber);

/// <summary>Card metadata safe to persist and display — never a PAN, never the JWE, never the
/// cardholder's name. MaskedPan is "**** 1234". Brand/MaskedPan are null when POK doesn't return
/// them (a SavedPaymentMethod then shows "Card on file"). ExpiryMonth/ExpiryYear are always null
/// with POK, which keeps no expiry date for a tokenized card.</summary>
public sealed record PokTokenizedCard(
    string CardTokenId, string? Brand, string? MaskedPan, string? ExpiryMonth, string? ExpiryYear);

/// <summary>Maps 1:1 onto the frontend POK SDK's own `PayerAuthentication` TypeScript type —
/// field names are intentionally identical so the API response can be passed through with no
/// remapping.</summary>
public sealed record PokPayerAuthSetup(
    string PayerAuthSetupReferenceId, PokDeviceDataCollection? DeviceDataCollection);

public sealed record PokDeviceDataCollection(string Url, string AccessToken);
