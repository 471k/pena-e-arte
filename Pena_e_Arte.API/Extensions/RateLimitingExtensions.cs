using System.Security.Claims;
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

                ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
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
                    IDatabase db = redis.GetDatabase();
                    ILogger logger = loggerFactory.CreateLogger("Pena_e_Arte.RateLimiter");

                    //   Policy name    | Requests | Window
                    // ─────────────────────────────────────
                    //   auth           |    10    | 1 min   ← login, register, oauth, forgot-password,
                    //                                          reset-password, refresh, verify-email
                    //   public-write   |    30    | 1 min   ← review submit, artist view tracking
                    //   public-booking |     8    | 5 min   ← guest booking submit + presign
                    //   public-read    |   120    | 1 min   ← portfolio feed, studio/artist pages
                    //   billing        |    20    | 1 min   ← Stripe-calling billing mutations, per user

                    AddRedisPolicy(opt, db, logger, "auth", permitLimit: 10, window: TimeSpan.FromMinutes(1));
                    AddRedisPolicy(opt, db, logger, "public-write", permitLimit: 30, window: TimeSpan.FromMinutes(1));
                    AddRedisPolicy(opt, db, logger, "public-booking", permitLimit: 8, window: TimeSpan.FromMinutes(5));
                    AddRedisPolicy(opt, db, logger, "public-read", permitLimit: 120, window: TimeSpan.FromMinutes(1));
                    AddRedisPolicy(
                        opt, db, logger, "billing", permitLimit: 20, window: TimeSpan.FromMinutes(1),
                        partitionKeySelector: BillingPartitionKey);
                });

        return services;
    }

    // Every "billing" endpoint requires authentication, so partitioning by user id (rather than
    // IP) is both more precise and immune to many users sharing one office/NAT IP. Reads the
    // claim directly instead of resolving ICurrentUser, matching CurrentUserService's own
    // extraction logic — this callback runs inside UseRateLimiter, which Program.cs now places
    // after UseAuthentication specifically so HttpContext.User is already populated here
    // (verified empirically: with the old ordering, IsAuthenticated was still false at this
    // point for every policy, so a user-id claim would never have been present).
    private static string BillingPartitionKey(HttpContext httpContext) =>
        httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";

    private static void AddRedisPolicy(
        RateLimiterOptions opt,
        IDatabase redis,
        ILogger logger,
        string policyName,
        int permitLimit,
        TimeSpan window,
        Func<HttpContext, string>? partitionKeySelector = null)
    {
        opt.AddPolicy<string>(policyName, httpContext =>
        {
            // Partition key = client IP by default. X-Forwarded-For is NOT trusted here
            // directly — ForwardedHeadersMiddleware (Program.cs) rewrites RemoteIpAddress to
            // the real client IP before this policy runs.
            string partitionKey = partitionKeySelector is not null
                ? partitionKeySelector(httpContext)
                : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            string redisKey = $"rl:{policyName}:{partitionKey}";

            return RateLimitPartition.Get(
                partitionKey: partitionKey,
                factory: _ => new RedisFixedWindowRateLimiter(
                    redis, redisKey, permitLimit, window, logger));
        });
    }
}
