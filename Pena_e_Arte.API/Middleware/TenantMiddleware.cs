using System.Security.Claims;
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
        HttpContext                 context,
        ICurrentTenant             tenant,
        ISubscriptionAccessService subscriptions)
    {
        Claim? claim = context.User.FindFirst("tenant_id");
        if (claim is not null && Guid.TryParse(claim.Value, out Guid studioId))
        {
            tenant.SetTenant(studioId);

            if (!context.User.IsInRole("issuer") && !IsExemptPath(context.Request.Path))
                await EnforceAsync(context, studioId, subscriptions);
        }

        await next(context);
    }

    private static bool IsExemptPath(PathString path) =>
        ExemptPrefixes.Any(prefix =>
            path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    private static async Task EnforceAsync(
        HttpContext                 context,
        Guid                       studioId,
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
        if (snapshot.Status == SubscriptionStatus.Trialing && now < snapshot.TrialExpiresAt) return;

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
