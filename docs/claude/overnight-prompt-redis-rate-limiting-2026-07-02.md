# Overnight Prompt — Redis-Backed Distributed Rate Limiting
**Date:** 2026-07-02
**Scope:** Infrastructure hardening. No new NuGet packages. No endpoint changes. No migrations.

---

## Problem

`RateLimitingExtensions.cs` currently uses `RateLimitPartition.GetFixedWindowLimiter`.
That limiter's token bucket lives inside the API process's heap.

With multiple replicas (K3s, rolling deploys, HPA scale-out), each pod tracks its own
counter independently. A client can hit `permitLimit × N_pods` requests before any
single pod rejects them. The limiter is effectively useless at scale.

**Redis is already a singleton in DI** (`IConnectionMultiplexer` registered in
`InfrastructureServiceExtensions.cs`). Use it.

---

## Required Reading

```
CLAUDE.md
docs/claude/backend.md
docs/claude/conventions.md
Pena_e_Arte.API/Extensions/RateLimitingExtensions.cs     ← replace this
Pena_e_Arte.Infrastructure/Extensions/InfrastructureServiceExtensions.cs  ← Redis DI
Pena_e_Arte.API/Endpoints/AuthEndpoints.cs               ← uses RequireRateLimiting("auth")
Pena_e_Arte.API/Endpoints/PublicEndpoints.cs             ← uses public-read / public-write
```

---

## Constraints

- No new NuGet packages. `StackExchange.Redis` is already a dependency.
- No changes to any endpoint file. `.RequireRateLimiting("auth")` etc. stay exactly as-is.
- No changes to `Program.cs`. `AddApiRateLimiting()` and `UseRateLimiter()` calls stay.
- No new migrations. This is pure in-memory/Redis infrastructure.
- Fail open: if Redis is unreachable, allow the request and log a warning.
  A Redis blip must NEVER take down the API.
- Never log the client IP in the error message. Log only `request_id` context (already
  on the Serilog scope from `RequestIdMiddleware`).

---

## Architecture

The ASP.NET Core `AddRateLimiter` + `RequireRateLimiting` pipeline is built on
`System.Threading.RateLimiting.RateLimiter` (abstract class). Endpoint metadata
stores the policy name. At request time the middleware resolves the named policy,
calls `WaitAndAcquireAsync` on the `PartitionedRateLimiter`, and rejects with 429 if
the lease is not acquired.

We replace the in-memory limiter factory (`GetFixedWindowLimiter`) with a factory
that creates a `RedisFixedWindowRateLimiter` per partition key. The `PartitionedRateLimiter`
caches one instance per unique IP per policy. Since all state lives in Redis (not in
the instance), creating one lightweight wrapper object per IP is perfectly fine.

The Redis key format: `rl:{policyName}:{clientIp}` — scoped to one policy + one IP.

The algorithm is a **fixed window** implemented via an atomic Lua script:
```lua
local count = redis.call('INCR', KEYS[1])
if count == 1 then
    redis.call('EXPIRE', KEYS[1], ARGV[2])   -- set TTL on first hit only
end
local ttl = redis.call('TTL', KEYS[1])
return {count, ttl}
```

INCR + conditional EXPIRE + TTL read are executed as one atomic unit by Redis.
The TTL is returned so the rejected lease can set `Retry-After` correctly.

---

## Step 1 — Create `RedisFixedWindowRateLimiter.cs`

File: `Pena_e_Arte.API/Extensions/RedisFixedWindowRateLimiter.cs`

```csharp
using System.Threading.RateLimiting;
using StackExchange.Redis;

namespace Pena_e_Arte.API.Extensions;

/// <summary>
/// A Redis-backed fixed-window rate limiter.
/// All state lives in Redis; this object is a stateless wrapper.
/// One instance is cached per partition key (client IP) by PartitionedRateLimiter.
/// Fails open when Redis is unreachable.
/// </summary>
internal sealed class RedisFixedWindowRateLimiter : RateLimiter
{
    private readonly IDatabase _redis;
    private readonly string    _key;
    private readonly int       _permitLimit;
    private readonly TimeSpan  _window;
    private readonly ILogger   _logger;

    // Atomic Lua: INCR key; set TTL on first hit; return {count, ttl}
    private const string LuaScript = """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then
            redis.call('EXPIRE', KEYS[1], ARGV[2])
        end
        local ttl = redis.call('TTL', KEYS[1])
        return {count, ttl}
        """;

    public RedisFixedWindowRateLimiter(
        IDatabase redis,
        string    key,
        int       permitLimit,
        TimeSpan  window,
        ILogger   logger)
    {
        _redis       = redis;
        _key         = key;
        _permitLimit = permitLimit;
        _window      = window;
        _logger      = logger;
    }

    // Tell PartitionedRateLimiter to evict idle instances after the window expires.
    // This prevents unbounded memory growth with large numbers of unique client IPs.
    public override TimeSpan? IdleDuration => _window;

    // ── Core methods ────────────────────────────────────────────────────────────

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        // Synchronous path — used by the ASP.NET middleware when not awaiting.
        try
        {
            RedisResult result = _redis.ScriptEvaluate(
                LuaScript,
                new RedisKey[] { _key },
                new RedisValue[] { _permitLimit, (long)_window.TotalSeconds });

            return EvaluateResult(result);
        }
        catch (Exception ex) when (IsRedisException(ex))
        {
            _logger.LogWarning("Redis rate limiter unavailable — failing open");
            return SuccessfulLease.Instance;
        }
    }

    protected override async ValueTask<RateLimitLease> WaitAndAcquireAsyncCore(
        int permitCount, CancellationToken cancellationToken)
    {
        // Primary path used by ASP.NET Core rate-limiter middleware.
        // No queuing — acquire or reject immediately.
        try
        {
            RedisResult result = await _redis.ScriptEvaluateAsync(
                LuaScript,
                new RedisKey[] { _key },
                new RedisValue[] { _permitLimit, (long)_window.TotalSeconds });

            return EvaluateResult(result);
        }
        catch (Exception ex) when (IsRedisException(ex))
        {
            _logger.LogWarning("Redis rate limiter unavailable — failing open");
            return SuccessfulLease.Instance;
        }
    }

    // ── Statistics (best-effort, used by metrics/diagnostics) ───────────────────

    public override RateLimiterStatistics? GetStatistics()
    {
        try
        {
            RedisValue val  = _redis.StringGet(_key);
            long current    = val.HasValue && long.TryParse(val, out long n) ? n : 0;
            long available  = Math.Max(0, _permitLimit - current);
            return new RateLimiterStatistics
            {
                CurrentAvailablePermits = available,
                CurrentQueuedCount      = 0,
                TotalSuccessfulLeases   = 0, // not tracked per-instance
                TotalFailedLeases       = 0,
            };
        }
        catch
        {
            return null;
        }
    }

    protected override void Dispose(bool disposing) { /* stateless — nothing to release */ }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private RateLimitLease EvaluateResult(RedisResult result)
    {
        RedisResult[] arr  = (RedisResult[])result!;
        long count = (long)arr[0];
        long ttl   = (long)arr[1]; // seconds remaining; -1 means no TTL (shouldn't happen)

        return count <= _permitLimit
            ? SuccessfulLease.Instance
            : new FailedLease(TimeSpan.FromSeconds(Math.Max(ttl, 1)));
    }

    private static bool IsRedisException(Exception ex) =>
        ex is RedisConnectionException
           or RedisTimeoutException
           or RedisException;

    // ── Lease implementations ────────────────────────────────────────────────────

    private sealed class SuccessfulLease : RateLimitLease
    {
        public static readonly SuccessfulLease Instance = new();
        public override bool            IsAcquired    => true;
        public override IEnumerable<string> MetadataNames => [];
        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
        protected override void Dispose(bool disposing) { }
    }

    private sealed class FailedLease : RateLimitLease
    {
        private readonly TimeSpan _retryAfter;

        public FailedLease(TimeSpan retryAfter) => _retryAfter = retryAfter;

        public override bool IsAcquired => false;
        public override IEnumerable<string> MetadataNames => [MetadataName.RetryAfter];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (metadataName == MetadataName.RetryAfter)
            {
                metadata = _retryAfter;
                return true;
            }
            metadata = null;
            return false;
        }

        protected override void Dispose(bool disposing) { }
    }
}
```

---

## Step 2 — Replace `RateLimitingExtensions.cs`

File: `Pena_e_Arte.API/Extensions/RateLimitingExtensions.cs`

**Replace the entire file** (existing three in-memory policies → three Redis-backed ones):

```csharp
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using StackExchange.Redis;

namespace Pena_e_Arte.API.Extensions;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        // Configure the global rejection code and OnRejected handler.
        // Policies are added via PostConfigure so IConnectionMultiplexer is available.
        services.AddRateLimiter(opt =>
        {
            opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Write Retry-After header on rejection so clients know when to retry.
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

        // PostConfigure<TDep1, TDep2> resolves both dependencies from DI at startup.
        // This is the correct pattern for options that depend on other services.
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
            // Partition key = policy name + client IP.
            // X-Forwarded-For is NOT trusted here — use the real socket IP.
            // Nginx/K8s ingress is responsible for IP normalisation before the request
            // reaches the API. If you need to trust X-Forwarded-For, configure
            // ForwardedHeadersMiddleware in Program.cs BEFORE UseRateLimiter().
            string clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            string redisKey = $"rl:{policyName}:{clientIp}";

            return RateLimitPartition.Get<string, RedisFixedWindowRateLimiter>(
                partitionKey: clientIp,
                factory: _ => new RedisFixedWindowRateLimiter(
                    redis, redisKey, permitLimit, window, logger));
        });
    }
}
```

---

## Step 3 — Verify endpoint declarations unchanged

Open each of these files and confirm **no changes are needed**:
- `AuthEndpoints.cs` — still has `.RequireRateLimiting("auth")` on login, register, oauth, forgot-password
- `PublicEndpoints.cs` — still has `.RequireRateLimiting("public-read")` and `.RequireRateLimiting("public-write")`
- `PublicDesignEndpoints.cs` — still has `.RequireRateLimiting("public-read")`
- `StudioEndpoints.cs` — still has `.RequireRateLimiting("public-read")` and `.RequireRateLimiting("auth")`

If any endpoint is missing a `RequireRateLimiting` call, add the most appropriate policy.
Do NOT add rate limiting to authenticated-only endpoints (owner/artist/issuer) — those are
protected by JWT and subject to much lower natural traffic. Only public/auth routes need it.

---

## Step 4 — Check `ForwardedHeaders` (important for K8s)

File: `Pena_e_Arte.API/Program.cs`

In a K8s/Nginx setup, the API pod only sees the ingress controller's IP in
`Connection.RemoteIpAddress`. Without trusted proxy support, every request appears to
come from the same IP (the ingress), and one rate limit bucket is shared by ALL clients.

Check if `ForwardedHeadersMiddleware` is already configured:
```bash
grep -n "ForwardedHeaders\|UseForwardedHeaders\|KnownProxies" \
    "Pena_e_Arte.API/Program.cs"
```

**If NOT found**, add it at the TOP of the middleware pipeline (BEFORE `UseRateLimiter`):

```csharp
// In Program.cs, before app.UseMiddleware<RequestIdMiddleware>():
app.UseForwardedHeaders(new Microsoft.AspNetCore.HttpOverrides.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
    // Only trust the immediate upstream (Nginx ingress).
    // Add your ingress controller's cluster IP here when known.
    // KnownNetworks and KnownProxies are empty by default — meaning ALL proxies are
    // trusted, which is acceptable for a private K8s cluster network. In production,
    // tighten to the actual ingress CIDR.
});
```

After adding `UseForwardedHeaders`, `httpContext.Connection.RemoteIpAddress` will
return the real client IP from `X-Forwarded-For`, making the per-IP partition key
correct.

**If already found**, verify the configuration matches the above and move on.

---

## Step 5 — Unit Tests

File: `tests/Pena_e_Arte.UnitTests/RateLimiting/RedisFixedWindowRateLimiterTests.cs`

Check which mocking library the unit test project uses:
```bash
grep -i "nsubstitute\|moq\|fakeiteasy" \
    "tests/Pena_e_Arte.UnitTests/Pena_e_Arte.UnitTests.csproj"
```

Use whichever is already there. The examples below use NSubstitute — adapt if needed.

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Pena_e_Arte.API.Extensions;
using StackExchange.Redis;
using System.Threading.RateLimiting;
// + NSubstitute or Moq

namespace Pena_e_Arte.UnitTests.RateLimiting;

public class RedisFixedWindowRateLimiterTests
{
    private const int       Limit  = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    // ── Helper ───────────────────────────────────────────────────────────────────

    private static RedisFixedWindowRateLimiter Create(IDatabase redis) =>
        new(redis, "rl:test:127.0.0.1", Limit, Window, NullLogger.Instance);

    // Builds a mock RedisResult[] that looks like {count, ttl}
    // NSubstitute example — replace with Moq equivalent if needed.
    private static IDatabase MockRedis(long count, long ttl = 30)
    {
        IDatabase db = Substitute.For<IDatabase>();
        RedisResult[] resultArray = [RedisResult.Create(count), RedisResult.Create(ttl)];
        RedisResult redisResult   = RedisResult.Create(resultArray, ResultType.Array);

        db.ScriptEvaluate(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns(redisResult);

        db.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(redisResult));

        return db;
    }

    // ── Tests ────────────────────────────────────────────────────────────────────

    // 1. Under the limit → lease acquired
    [Fact]
    public async Task WaitAndAcquireAsync_UnderLimit_AcquiresLease()
    {
        IDatabase db      = MockRedis(count: 3, ttl: 45);
        var       limiter = Create(db);

        RateLimitLease lease = await limiter.WaitAndAcquireAsync(1);

        Assert.True(lease.IsAcquired);
    }

    // 2. Exactly at the limit → lease acquired (boundary: count == permitLimit is allowed)
    [Fact]
    public async Task WaitAndAcquireAsync_AtLimit_AcquiresLease()
    {
        IDatabase db      = MockRedis(count: Limit, ttl: 10);
        var       limiter = Create(db);

        RateLimitLease lease = await limiter.WaitAndAcquireAsync(1);

        Assert.True(lease.IsAcquired);
    }

    // 3. Over the limit → lease rejected
    [Fact]
    public async Task WaitAndAcquireAsync_OverLimit_RejectsLease()
    {
        IDatabase db      = MockRedis(count: Limit + 1, ttl: 20);
        var       limiter = Create(db);

        RateLimitLease lease = await limiter.WaitAndAcquireAsync(1);

        Assert.False(lease.IsAcquired);
    }

    // 4. Retry-After header set correctly on rejected lease
    [Fact]
    public async Task WaitAndAcquireAsync_OverLimit_SetsRetryAfter()
    {
        IDatabase db      = MockRedis(count: Limit + 1, ttl: 42);
        var       limiter = Create(db);

        RateLimitLease lease = await limiter.WaitAndAcquireAsync(1);

        Assert.True(lease.TryGetMetadata(MetadataName.RetryAfter, out object? meta));
        TimeSpan retryAfter = Assert.IsType<TimeSpan>(meta);
        Assert.Equal(42, (int)retryAfter.TotalSeconds);
    }

    // 5. Retry-After falls back to at least 1 second when TTL is negative
    [Fact]
    public async Task WaitAndAcquireAsync_NegativeTtl_RetryAfterIsAtLeastOne()
    {
        IDatabase db      = MockRedis(count: Limit + 1, ttl: -1);
        var       limiter = Create(db);

        RateLimitLease lease = await limiter.WaitAndAcquireAsync(1);

        Assert.False(lease.IsAcquired);
        Assert.True(lease.TryGetMetadata(MetadataName.RetryAfter, out object? meta));
        TimeSpan retryAfter = Assert.IsType<TimeSpan>(meta);
        Assert.True(retryAfter.TotalSeconds >= 1);
    }

    // 6. Redis throws RedisConnectionException → fail open (request allowed)
    [Fact]
    public async Task WaitAndAcquireAsync_RedisUnavailable_FailsOpen()
    {
        IDatabase db = Substitute.For<IDatabase>();
        db.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns<Task<RedisResult>>(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "test"));

        var limiter = Create(db);

        RateLimitLease lease = await limiter.WaitAndAcquireAsync(1);

        Assert.True(lease.IsAcquired, "Should fail open when Redis is unreachable");
    }

    // 7. Redis throws RedisTimeoutException → fail open
    [Fact]
    public async Task WaitAndAcquireAsync_RedisTimeout_FailsOpen()
    {
        IDatabase db = Substitute.For<IDatabase>();
        db.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns<Task<RedisResult>>(_ => throw new RedisTimeoutException(OperationType.Write, null));

        var limiter = Create(db);

        RateLimitLease lease = await limiter.WaitAndAcquireAsync(1);

        Assert.True(lease.IsAcquired, "Should fail open on Redis timeout");
    }

    // 8. AttemptAcquireCore (sync path) — under limit → acquired
    [Fact]
    public void AttemptAcquire_UnderLimit_AcquiresLease()
    {
        IDatabase db      = MockRedis(count: 2, ttl: 55);
        var       limiter = Create(db);

        RateLimitLease lease = limiter.AttemptAcquire(1);

        Assert.True(lease.IsAcquired);
    }

    // 9. AttemptAcquireCore (sync path) — over limit → rejected
    [Fact]
    public void AttemptAcquire_OverLimit_RejectsLease()
    {
        IDatabase db      = MockRedis(count: Limit + 3, ttl: 15);
        var       limiter = Create(db);

        RateLimitLease lease = limiter.AttemptAcquire(1);

        Assert.False(lease.IsAcquired);
    }

    // 10. AttemptAcquireCore (sync path) — Redis unavailable → fail open
    [Fact]
    public void AttemptAcquire_RedisUnavailable_FailsOpen()
    {
        IDatabase db = Substitute.For<IDatabase>();
        db.ScriptEvaluate(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "test"));

        var limiter = Create(db);

        RateLimitLease lease = limiter.AttemptAcquire(1);

        Assert.True(lease.IsAcquired, "Should fail open on Redis error");
    }

    // 11. GetStatistics — returns current available permits from Redis
    [Fact]
    public void GetStatistics_ReturnsAvailablePermits()
    {
        IDatabase db = Substitute.For<IDatabase>();
        db.StringGet(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"3");

        var limiter = Create(db);

        RateLimiterStatistics? stats = limiter.GetStatistics();

        Assert.NotNull(stats);
        Assert.Equal(Limit - 3, stats!.CurrentAvailablePermits);
    }

    // 12. GetStatistics — Redis unavailable → returns null gracefully
    [Fact]
    public void GetStatistics_RedisUnavailable_ReturnsNull()
    {
        IDatabase db = Substitute.For<IDatabase>();
        db.StringGet(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "test"));

        var limiter = Create(db);

        RateLimiterStatistics? stats = limiter.GetStatistics();

        Assert.Null(stats);
    }

    // 13. IdleDuration equals the configured window
    [Fact]
    public void IdleDuration_EqualsConfiguredWindow()
    {
        IDatabase db      = MockRedis(1, 60);
        var       limiter = Create(db);

        Assert.Equal(Window, limiter.IdleDuration);
    }

    // 14. SuccessfulLease has no MetadataNames
    [Fact]
    public async Task SuccessfulLease_HasNoMetadata()
    {
        IDatabase db      = MockRedis(count: 1, ttl: 60);
        var       limiter = Create(db);

        RateLimitLease lease = await limiter.WaitAndAcquireAsync(1);

        Assert.True(lease.IsAcquired);
        Assert.Empty(lease.MetadataNames);
        Assert.False(lease.TryGetMetadata(MetadataName.RetryAfter, out _));
    }

    // 15. FailedLease exposes only RetryAfter metadata
    [Fact]
    public async Task FailedLease_MetadataNames_ContainsRetryAfter()
    {
        IDatabase db      = MockRedis(count: Limit + 1, ttl: 10);
        var       limiter = Create(db);

        RateLimitLease lease = await limiter.WaitAndAcquireAsync(1);

        Assert.False(lease.IsAcquired);
        Assert.Single(lease.MetadataNames, MetadataName.RetryAfter);
        Assert.False(lease.TryGetMetadata("unknown-key", out _));
    }
}
```

---

## Verification

Run in order. Fix every failure before the next step.

```bash
cd "Pena e Arte"

# 1. Backend compiles with no errors
dotnet build

# 2. All unit tests pass (including the 15 new rate limiter tests)
dotnet test tests/Pena_e_Arte.UnitTests/Pena_e_Arte.UnitTests.csproj --no-build

# 3. Full test suite passes
dotnet test --no-build

# 4. Confirm rate-limiter policies are registered (string match in logs or startup output)
dotnet run --project Pena_e_Arte.API -- --dry-run 2>&1 | grep -i "rate" || true
# If the app doesn't support --dry-run, skip step 4 and rely on step 3.
```

---

## Exit Condition

Steps 1–3 exit with code 0.

Then append to `docs/claude/architecture.md`:

```markdown
## Redis-Backed Distributed Rate Limiting — 2026-07-02

### Problem solved
ASP.NET Core's built-in `FixedWindowLimiter` is in-process. With N replicas,
each pod tracked its own counter — effective limit was N × permitLimit before
any pod rejected a request. Useless at scale.

### Solution
`RedisFixedWindowRateLimiter` — a custom `System.Threading.RateLimiting.RateLimiter`
subclass backed by a Redis atomic Lua script (INCR + EXPIRE + TTL in one round-trip).
One instance per (policy, client IP) pair, cached by `PartitionedRateLimiter`.
All state lives in Redis; the object is a stateless wrapper.

### Key decisions
- **No new NuGet packages** — `StackExchange.Redis` already in the project.
- **Fail open** — Redis blip allows the request through + logs a warning.
  A rate-limiter outage is not worth taking the API down.
- **Fixed window via INCR + EXPIRE** — simple, atomic, correct.
  The TTL returned from Redis is used as the `Retry-After` header value.
- **IdleDuration = window** — tells `PartitionedRateLimiter` to evict idle
  IP entries after the window expires, preventing memory leaks.
- **PostConfigure<IConnectionMultiplexer, ILoggerFactory>** — resolves Redis
  from DI without changing `AddApiRateLimiting()` signature or `Program.cs`.
- **ForwardedHeaders middleware** — added if absent, so `RemoteIpAddress`
  reflects the real client IP behind the K8s/Nginx ingress.

### Policies (unchanged limits)
| Policy       | Limit | Window | Endpoints                                    |
|---|---|---|---|
| auth         |  10   | 1 min  | login, register, oauth, forgot-password      |
| public-write |  30   | 1 min  | review submit, artist view tracking          |
| public-read  | 120   | 1 min  | portfolio feed, studio/artist pages, QR, map |

### Files changed
- `Pena_e_Arte.API/Extensions/RedisFixedWindowRateLimiter.cs` (NEW)
- `Pena_e_Arte.API/Extensions/RateLimitingExtensions.cs` (REPLACED)
- `Pena_e_Arte.API/Program.cs` (ForwardedHeaders added if missing)
- `tests/Pena_e_Arte.UnitTests/RateLimiting/RedisFixedWindowRateLimiterTests.cs` (NEW — 15 tests)

### No changes to
- Any endpoint file (RequireRateLimiting calls identical)
- Any migration
- Any NuGet dependency
```
