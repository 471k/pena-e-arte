using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.API.Middleware;

public class TenantMiddleware(RequestDelegate next)
{
    private static readonly string[] ExemptPrefixes =
    [
        "/api/v1/auth",
        "/api/v1/billing",
        "/api/v1/webhooks",
        "/api/studios/map",
        "/health",
        "/metrics",
        "/hangfire",
        "/hubs",
    ];

    private static readonly HashSet<string> WriteMethods =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    public async Task InvokeAsync(
        HttpContext context,
        ICurrentTenant tenant,
        ISubscriptionAccessService subscriptions,
        IAppDbContext db)
    {
        Claim? claim = context.User.FindFirst("tenant_id");
        if (claim is not null && Guid.TryParse(claim.Value, out Guid studioId))
        {
            tenant.SetTenant(studioId);

            // The entire enforcement mechanism for Support Impersonation — every RBAC
            // policy already grants "admin" every role-based permission a studio-scoped
            // endpoint checks (see AuthorizationExtensions.cs), so without this gate an
            // impersonation token would already reach every endpoint in the app. A no-op
            // for every normal request (no "imp" claim present). See ImpersonationAllowList
            // and docs/claude/architecture.md Decisions Log.
            Claim? impClaim = context.User.FindFirst("imp");
            if (impClaim is not null)
                await EnforceImpersonationScopeAsync(context, db, impClaim.Value);

            if (!context.User.IsInRole("admin") && !IsExemptPath(context.Request.Path))
                await EnforceAsync(context, studioId, subscriptions);
        }

        await next(context);
    }

    private static bool IsExemptPath(PathString path) =>
        ExemptPrefixes.Any(prefix =>
            path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    private static async Task EnforceImpersonationScopeAsync(
        HttpContext context, IAppDbContext db, string sessionIdClaimValue)
    {
        if (!Guid.TryParse(sessionIdClaimValue, out Guid sessionId))
            throw new ImpersonationScopeException("Invalid impersonation session.");

        if (!ImpersonationAllowList.IsAllowed(context.Request.Method, context.Request.Path))
            throw new ImpersonationScopeException(
                "This action is not available while impersonating a studio.");

        // Scoped by the ambient tenant filter (tenant.SetTenant(studioId) just ran above)
        // — no IgnoreQueryFilters() needed here, unlike StartImpersonationCommand /
        // EndImpersonationSessionCommand, which run under the admin's own (non-impersonating)
        // token with no tenant_id claim set at all.
        //
        // Checked against the DB on every request, not just the JWT's own "exp" claim — a
        // JWT can't be revoked by a DB update alone unless something checks a row like this
        // per request, which is what makes EndImpersonationSessionCommand's "End session"
        // take effect immediately rather than waiting for the token to naturally expire.
        ImpersonationSession? session = await db.ImpersonationSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, context.RequestAborted);

        if (session is null || !session.IsActive)
            throw new ImpersonationScopeException("This impersonation session has ended.");
    }

    private static async Task EnforceAsync(
        HttpContext context,
        Guid studioId,
        ISubscriptionAccessService subscriptions)
    {
        bool isActive = await subscriptions.IsStudioActiveAsync(studioId, context.RequestAborted);
        if (!isActive)
        {
            // GET /api/v1/studios/me passes through when suspended so the owner can
            // read isActive=false and the frontend can render the SuspensionBanner.
            // All other paths — including writes to this endpoint — remain blocked.
            if (context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                context.Request.Path.Equals("/api/v1/studios/me", StringComparison.OrdinalIgnoreCase))
                return;
            throw new TenantSuspendedException();
        }

        SubscriptionSnapshot? snapshot =
            await subscriptions.GetSnapshotAsync(studioId, context.RequestAborted);

        if (snapshot is null)
            throw new SubscriptionRequiredException("No active subscription found.");

        DateTime now = DateTime.UtcNow;

        if (snapshot.Status == SubscriptionStatus.Active) return;
        if (snapshot.Status == SubscriptionStatus.Trialing &&
            snapshot.TrialExpiresAt is DateTime trialExpiresAt && now < trialExpiresAt) return;

        if (snapshot.Status == SubscriptionStatus.GracePeriod && now < snapshot.GracePeriodEnd)
        {
            if (WriteMethods.Contains(context.Request.Method))
                throw new SubscriptionRequiredException(
                    "Your studio is in the read-only grace period. Subscribe to re-enable write access.");
            return;
        }

        if (snapshot.Status == SubscriptionStatus.PastDue)
            throw new SubscriptionRequiredException(
                "Your subscription payment is overdue. Please update your billing details.");

        throw new SubscriptionRequiredException(
            "Your studio subscription has expired. Please subscribe to continue.");
    }
}
