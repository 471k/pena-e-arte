using System.Text.RegularExpressions;

namespace Pena_e_Arte.API.Middleware;

/// <summary>
/// The entire security surface for Support Impersonation's route scope. Deliberately a
/// narrow, explicit allow-list (deny by default) rather than a blanket "all GETs" rule or
/// per-endpoint attributes scattered across 25 files — a missed allow-list entry is an
/// inconvenience, a missed deny-list entry is a security bug. Extending it later is a
/// small, one-line, easy-to-review change; see docs/claude/architecture.md Decisions Log
/// — "Support Impersonation with Audit Trail" for the product/security sign-off this list
/// was built against (client medical/PII and all financial data denied entirely).
///
/// GET only — every POST/PUT/PATCH/DELETE is denied while impersonating, full stop, no
/// exceptions in this phase. Enforced by TenantMiddleware on every request carrying an
/// "imp" claim.
/// </summary>
public static class ImpersonationAllowList
{
    private static readonly Regex Guid = new("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

    // Route paths, NOT method-prefixed strings — every entry here is implicitly GET-only,
    // enforced by the check in IsAllowed below rather than repeated per row.
    private static readonly Regex[] AllowedGetRoutes =
    [
        Anchored("/api/v1/appointments/?"),
        Anchored($"/api/v1/appointments/{Guid}"),
        Anchored("/api/v1/appointments/check-slot"),
        Anchored("/api/v1/artists/?"),
        Anchored($"/api/v1/artists/{Guid}"),
        Anchored($"/api/v1/artists/{Guid}/schedule"),
        Anchored("/api/v1/clients/?"),
        Anchored($"/api/v1/clients/{Guid}"),
        Anchored("/api/v1/studios/me"),
        Anchored($"/api/v1/studios/{Guid}/closures"),
        Anchored("/api/v1/deposit-rules/?"),
        Anchored($"/api/v1/deposit-rules/{Guid}"),
        // NOTE: the original spec text said "/api/v1/manual-reminders" — the actual route
        // group (see ManualReminderEndpoints.cs) is "/api/v1/reminders". Corrected here,
        // same kind of stale-reference fix as the IssuerStudioDetailPage.tsx rename.
        Anchored("/api/v1/reminders/?"),
        Anchored("/api/v1/notifications/?"),
    ];

    private static Regex Anchored(string pattern) => new($"^{pattern}$", RegexOptions.IgnoreCase);

    public static bool IsAllowed(string method, PathString path)
    {
        if (!HttpMethods.IsGet(method)) return false;

        string value = path.Value ?? string.Empty;
        return AllowedGetRoutes.Any(r => r.IsMatch(value));
    }
}
