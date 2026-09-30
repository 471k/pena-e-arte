namespace Pena_e_Arte.Contracts.Responses;

/// <summary>PokEnvironment is the single source of truth for which POK environment ("staging" /
/// "production") the client-side checkout widget must target — the frontend must never derive
/// this independently (e.g. from its own build mode), since that can drift out of sync with the
/// backend's actual configured host and POK 401s on a mismatch.
///
/// Currency is always populated (the studio's currency) so the frontend never has to ask twice.
/// CardUnavailableReason is one of <see cref="CardUnavailableReasons"/> whenever
/// CardPaymentsAvailable is false, so the UI can show the right explanation instead of a generic
/// "unavailable" message.</summary>
public record PaymentCapabilitiesResponse(
    bool CardPaymentsAvailable,
    string? PokEnvironment = null,
    string? Currency = null,
    string? CardUnavailableReason = null);

/// <summary>String constants for <see cref="PaymentCapabilitiesResponse.CardUnavailableReason"/> —
/// a closed set the frontend switches on, not a free-text message (copy stays a frontend
/// concern).</summary>
public static class CardUnavailableReasons
{
    public const string ProviderUnsupportedCurrency = "provider_unsupported_currency";
    public const string ProviderNotConnected = "provider_not_connected";
    public const string ProviderDisabled = "provider_disabled";
}
