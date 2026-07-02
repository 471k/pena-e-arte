using Microsoft.Extensions.Diagnostics.HealthChecks;
using Stripe;

namespace Pena_e_Arte.API.Extensions;

public class StripeHealthCheck(BalanceService balanceService) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await balanceService.GetAsync(cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (StripeException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return HealthCheckResult.Unhealthy("Stripe API key invalid or unauthorised", ex);
        }
        catch (StripeException ex)
        {
            return HealthCheckResult.Degraded("Stripe API error", ex);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Stripe unreachable", ex);
        }
    }
}
