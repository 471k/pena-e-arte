namespace Pena_e_Arte.Contracts.Responses;

/// <summary>Amount/Currency let the checkout page render the server's own figures instead of
/// trusting a forgeable "amount" query-string param (docs/claude/architecture.md Decisions Log,
/// "Studio currency").</summary>
public record PaymentClientTokenResponse(string ClientToken, decimal Amount, string Currency);
