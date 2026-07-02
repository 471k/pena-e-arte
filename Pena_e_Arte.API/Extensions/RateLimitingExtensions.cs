using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Pena_e_Arte.API.Extensions;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(opt =>
        {
            opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // NOTE: partitioned per client IP via AddPolicy below — AddFixedWindowLimiter's
            // shorthand form would share one global bucket across every caller, letting a
            // single client exhaust the limit for all anonymous users.
            AddPerIpFixedWindowPolicy(opt, "auth",         permitLimit: 10,  window: TimeSpan.FromMinutes(1));
            AddPerIpFixedWindowPolicy(opt, "public-write", permitLimit: 30,  window: TimeSpan.FromMinutes(1));
            AddPerIpFixedWindowPolicy(opt, "public-read",  permitLimit: 120, window: TimeSpan.FromMinutes(1));
        });

        return services;
    }

    private static void AddPerIpFixedWindowPolicy(
        RateLimiterOptions opt, string policyName, int permitLimit, TimeSpan window)
    {
        opt.AddPolicy(policyName, httpContext =>
        {
            string partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
            {
                Window               = window,
                PermitLimit          = permitLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0,
                AutoReplenishment    = true,
            });
        });
    }
}
