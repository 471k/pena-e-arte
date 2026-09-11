using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.API.Authentication;

public class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    /// <summary>The claim every endpoint behind the "ExternalApiAccess" policy checks for —
    /// deliberately not a role, so this principal is never accidentally accepted by an
    /// existing role-based policy (OwnerOnly, ArtistAndAbove, etc.), and a normal JWT
    /// principal never accidentally satisfies this one.</summary>
    public const string ScopeClaimType = "scope";
    public const string ExternalApiScope = "external-api";
}

/// <summary>
/// Authenticates a request via the X-Api-Key header against StudioApiKey.KeyHash. On
/// success, produces a ClaimsPrincipal carrying a tenant_id claim — TenantMiddleware (which
/// runs after authentication, unchanged) picks that up exactly the same way it does for a
/// normal JWT, so the rest of the request pipeline (tenant-scoped EF Core query filters,
/// subscription/suspension enforcement) applies with no special-casing.
/// </summary>
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IAppDbContext db)
    : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationSchemeOptions.HeaderName, out var headerValues))
            return AuthenticateResult.NoResult();

        string? rawKey = headerValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(rawKey))
            return AuthenticateResult.Fail("Missing API key.");

        string keyHash = ApiKeyHasher.Hash(rawKey);

        // Approved cross-tenant lookup: the caller's studio isn't known yet — same shape as
        // resolving a refresh token or a StudioJoinInvite by opaque value before any tenant
        // context exists.
        StudioApiKey? key = await db.StudioApiKeys
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash && k.RevokedAt == null, Context.RequestAborted);

        if (key is null)
            return AuthenticateResult.Fail("Invalid or revoked API key.");

        key.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(Context.RequestAborted);

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, key.Id.ToString()),
            new("tenant_id", key.StudioId.ToString()),
            new(ApiKeyAuthenticationSchemeOptions.ScopeClaimType, ApiKeyAuthenticationSchemeOptions.ExternalApiScope),
        ];

        ClaimsIdentity identity = new(claims, ApiKeyAuthenticationSchemeOptions.Scheme);
        ClaimsPrincipal principal = new(identity);
        AuthenticationTicket ticket = new(principal, ApiKeyAuthenticationSchemeOptions.Scheme);

        return AuthenticateResult.Success(ticket);
    }
}
