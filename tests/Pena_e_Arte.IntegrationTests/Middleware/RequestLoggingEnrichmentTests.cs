using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Pena_e_Arte.API.Middleware;
using Serilog;

namespace Pena_e_Arte.IntegrationTests.Middleware;

public class RequestLoggingEnrichmentTests
{
    [Fact]
    public void Enrich_AlwaysSetsRequestId()
    {
        IDiagnosticContext diagnosticContext = Substitute.For<IDiagnosticContext>();
        DefaultHttpContext httpContext = new() { TraceIdentifier = "req-1" };

        RequestLoggingEnrichment.Enrich(diagnosticContext, httpContext);

        diagnosticContext.Received(1).Set("request_id", "req-1", false);
    }

    [Fact]
    public void Enrich_Unauthenticated_DoesNotSetUserIdOrTenantId()
    {
        IDiagnosticContext diagnosticContext = Substitute.For<IDiagnosticContext>();
        DefaultHttpContext httpContext = new() { TraceIdentifier = "req-2" };

        RequestLoggingEnrichment.Enrich(diagnosticContext, httpContext);

        diagnosticContext.DidNotReceive().Set("user_id", Arg.Any<object>(), Arg.Any<bool>());
        diagnosticContext.DidNotReceive().Set("tenant_id", Arg.Any<object>(), Arg.Any<bool>());
    }

    [Fact]
    public void Enrich_Authenticated_SetsUserIdAndTenantIdFromClaims()
    {
        IDiagnosticContext diagnosticContext = Substitute.For<IDiagnosticContext>();
        DefaultHttpContext httpContext = new() { TraceIdentifier = "req-3" };
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, "user-42"),
            new("tenant_id", "studio-7"),
        ];
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        RequestLoggingEnrichment.Enrich(diagnosticContext, httpContext);

        diagnosticContext.Received(1).Set("user_id", "user-42", false);
        diagnosticContext.Received(1).Set("tenant_id", "studio-7", false);
    }

    [Fact]
    public void Enrich_AuthenticatedWithoutTenantClaim_SetsUserIdOnly()
    {
        // e.g. an issuer (cross-tenant platform admin) token has no tenant_id claim.
        IDiagnosticContext diagnosticContext = Substitute.For<IDiagnosticContext>();
        DefaultHttpContext httpContext = new() { TraceIdentifier = "req-4" };
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, "issuer-1")];
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        RequestLoggingEnrichment.Enrich(diagnosticContext, httpContext);

        diagnosticContext.Received(1).Set("user_id", "issuer-1", false);
        diagnosticContext.DidNotReceive().Set("tenant_id", Arg.Any<object>(), Arg.Any<bool>());
    }
}
