namespace Pena_e_Arte.Domain.Exceptions;

/// <summary>
/// Thrown by TenantMiddleware's impersonation gate — distinct from ForbiddenException so
/// it's unambiguous in logs which mechanism blocked the request. Covers both a route not
/// on the impersonation allow-list AND a session that is no longer active (ended early or
/// past its hard-capped expiry), since both are enforced by the same per-request DB check.
/// </summary>
public class ImpersonationScopeException(string message) : DomainException(message);
