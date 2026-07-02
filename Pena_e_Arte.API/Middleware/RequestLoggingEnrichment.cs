using System.Security.Claims;
using Serilog;

namespace Pena_e_Arte.API.Middleware;

/// <summary>
/// Tags the per-request HTTP summary log line with request_id (always) and
/// user_id/tenant_id (once authentication has run). Never enrich with PII
/// (names, emails, phone numbers, card data) — see CLAUDE.md logging rule.
/// </summary>
public static class RequestLoggingEnrichment
{
    public static void Enrich(IDiagnosticContext diagnosticContext, HttpContext httpContext)
    {
        diagnosticContext.Set("request_id", httpContext.TraceIdentifier);

        if (httpContext.User.Identity?.IsAuthenticated != true) return;

        string? userId   = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        string? tenantId = httpContext.User.FindFirst("tenant_id")?.Value;
        if (userId   is not null) diagnosticContext.Set("user_id",   userId);
        if (tenantId is not null) diagnosticContext.Set("tenant_id", tenantId);
    }
}
