namespace Pena_e_Arte.Contracts.Responses;

/// <summary>PokEnvironment is the single source of truth for which POK environment ("staging" /
/// "production") the client-side checkout widget must target — the frontend must never derive
/// this independently (e.g. from its own build mode), since that can drift out of sync with the
/// backend's actual configured host and POK 401s on a mismatch.</summary>
public record PaymentCapabilitiesResponse(bool CardPaymentsAvailable, string? PokEnvironment = null);
