using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using StackExchange.Redis;

namespace Pena_e_Arte.API.Extensions;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        // Configure the global rejection code and OnRejected handler. Policies are added
        // via PostConfigure below so IConnectionMultiplexer is available from DI.
        services.AddRateLimiter(opt =>
        {
            opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            opt.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                    ctx.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString();

                ctx.HttpContext.Response.StatusCode  = StatusCodes.Status429TooManyRequests;
                ctx.HttpContext.Response.ContentType = "application/json";
                await ctx.HttpContext.Response.WriteAsync(
                    """{"status":429,"message":"Too many requests. Please slow down."}""", ct);
            };
        });

        // PostConfigure<TDep1, TDep2> resolves both dependencies from DI at startup —
        // the correct pattern for options that depend on other services.
        services.AddOptions<RateLimiterOptions>()
            .PostConfigure<IConnectionMultiplexer, ILoggerFactory>(
                (opt, redis, loggerFactory) =>
                {
                    IDatabase db     = redis.GetDatabase();
                    ILogger   logger = loggerFactory.CreateLogger("Pena_e_Arte.RateLimiter");

                    //   Policy name    | Requests | Window
                    // ─────────────────────────────────────
                    //   auth           |    10    | 1 min   ← login, register, oauth, forgot-password
                    //   public-write   |    30    | 1 min   ← review submit, artist view tracking
                    //   public-read    |   120    | 1 min   ← portfolio feed, studio/artist pages

                    AddRedisPolicy(opt, db, logger, "auth",         permitLimit: 10,  window: TimeSpan.FromMinutes(1));
                    AddRedisPolicy(opt, db, logger, "public-write", permitLimit: 30,  window: TimeSpan.FromMinutes(1));
                    AddRedisPolicy(opt, db, logger, "public-read",  permitLimit: 120, window: TimeSpan.FromMinutes(1));
                });

        return services;
    }

    private static void AddRedisPolicy(
        RateLimiterOptions opt,
        IDatabase          redis,
        ILogger            logger,
        string             policyName,
        int                permitLimit,
        TimeSpan           window)
    {
        opt.AddPolicy<string>(policyName, httpContext =>
        {
            // Partition key = client IP. X-Forwarded-For is NOT trusted here directly —
            // ForwardedHeadersMiddleware (Program.cs) rewrites RemoteIpAddress to the real
            // client IP before this policy runs.
            string clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            string redisKey = $"rl:{policyName}:{clientIp}";

            return RateLimitPartition.Get(
                partitionKey: clientIp,
                factory: _ => new RedisFixedWindowRateLimiter(
                    redis, redisKey, permitLimit, window, logger));
        });
    }
}
