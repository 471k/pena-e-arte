using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Pena_e_Arte.Infrastructure.Hubs;

// JoinStudio validates the caller's own tenant_id claim against the requested studioId before
// adding them to the broadcast group — mirrors SupportHub.JoinTicket's fix. /hubs paths are
// exempt from TenantMiddleware (see TenantMiddleware.ExemptPrefixes), so ICurrentUser/
// ICurrentTenant are never populated for hub invocations; claims are read directly from
// Context.User instead. Issuer connections may join any studio's group for platform-support
// purposes, mirroring the issuer role's other documented cross-tenant reads.
[Authorize]
public class DesignHub : Hub
{
    public async Task JoinStudio(string studioId)
    {
        if (!Guid.TryParse(studioId, out Guid requestedStudioId)) return;

        string role = Context.User?.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (!string.Equals(role, "issuer", StringComparison.OrdinalIgnoreCase))
        {
            Guid callerStudioId = Guid.TryParse(
                Context.User?.FindFirstValue("tenant_id"), out Guid sid) ? sid : Guid.Empty;
            if (callerStudioId != requestedStudioId) return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"studio:{requestedStudioId}");
    }

    public async Task LeaveStudio(string studioId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"studio:{studioId}");
}
