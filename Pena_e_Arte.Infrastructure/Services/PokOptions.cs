namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Platform-level POK config — never a merchant credential (those are per-studio, resolved via
/// ISecretsProvider/StudioCredentialRef, see ADR-0001). Just which host to call.
/// </summary>
public class PokOptions
{
    public const string Section = "Pok";

    /// <summary>"https://api-staging.pokpay.io" or "https://api.pokpay.io" — never mix with
    /// credentials from the other environment (POK rejects the combination, usually with 401).</summary>
    public string BaseUrl { get; init; } = "https://api-staging.pokpay.io";

    /// <summary>This API's own publicly reachable base URL (e.g. "https://api.tattooos.co"),
    /// used to build the webhookUrl POK POSTs to on order events. Null/empty is valid — omitted
    /// entirely on unreachable-from-the-internet environments (local dev); PaymentReconciliationJob's
    /// normal schedule is still the source of truth either way (ADR-0001 — webhooks are triggers,
    /// never state).</summary>
    public string? WebhookCallbackBaseUrl { get; init; }
}
