using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Pena_e_Arte.API.Extensions;

public class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return Task.FromResult(redis.IsConnected
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Degraded("Redis not connected"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Redis error", ex));
        }
    }
}
