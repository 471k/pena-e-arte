using MediatR;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Common.Behaviors;

/// <summary>
/// Checks quota-checked commands (those implementing IQuotaCheckedCommand) against the
/// current tenant's Plan limits before the handler runs. Registered after
/// ValidationBehavior in Program.cs, so request shape is validated first.
/// </summary>
public class PlanLimitBehavior<TRequest, TResponse>(IPlanLimitService planLimits)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (request is IQuotaCheckedCommand quotaChecked)
            await planLimits.EnsureWithinLimitAsync(quotaChecked.QuotaType, ct);

        return await next(ct);
    }
}
