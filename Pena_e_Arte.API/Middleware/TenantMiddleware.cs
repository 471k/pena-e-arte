using System.Security.Claims;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.API.Middleware;

public class TenantMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ICurrentTenant tenant)
    {
        Claim? claim = context.User.FindFirst("tenant_id");
        if (claim is not null && Guid.TryParse(claim.Value, out Guid studioId))
            tenant.SetTenant(studioId);

        await next(context);
    }
}
