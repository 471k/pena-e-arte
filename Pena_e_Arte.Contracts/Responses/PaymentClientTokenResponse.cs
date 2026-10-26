namespace Pena_e_Arte.Contracts.Responses;

/// <summary>Amount/Currency let the checkout page render the server's own figures instead of
/// trusting a forgeable "amount" query-string param (docs/claude/architecture.md Decisions Log,
/// "Studio currency").</summary>
/// <param name="ReferenceAmount">The lek frozen on this payment when its order was created (Albanian EUR
/// studios only, else null): the checkout page shows exactly what is stored, spec D3.</param>
/// <param name="StudioCountryCode">The payment's studio country; with Currency it decides whether the
/// checkout page shows lek and the "charged in euros" line.</param>
public record PaymentClientTokenResponse(
    string ClientToken, decimal Amount, string Currency,
    decimal? ReferenceAmount = null, string? ReferenceCurrency = null, string? StudioCountryCode = null);
