using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace Pena_e_Arte.UnitTests.Helpers;

internal static class FakeHubCallerContext
{
    public static HubCallerContext Build(string connectionId, Guid? tenantId, string? role)
    {
        List<Claim> claims = [];
        if (tenantId is not null) claims.Add(new Claim("tenant_id", tenantId.Value.ToString()));
        if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
        ClaimsPrincipal user = new(new ClaimsIdentity(claims, "test"));

        HubCallerContext context = Substitute.For<HubCallerContext>();
        context.User.Returns(user);
        context.ConnectionId.Returns(connectionId);
        return context;
    }
}
