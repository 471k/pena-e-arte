using System.Threading.RateLimiting;
using StackExchange.Redis;

namespace Pena_e_Arte.API.Extensions;

// Redis-backed fixed-window rate limiter. All state lives in Redis; this object is a
// stateless wrapper cached per partition key (client IP) by PartitionedRateLimiter.
// Fails open when Redis is unreachable so a Redis blip never takes down the API.
internal sealed class RedisFixedWindowRateLimiter : RateLimiter
{
    private readonly IDatabase _redis;
    private readonly string _key;
    private readonly int _permitLimit;
    private readonly TimeSpan _window;
    private readonly ILogger _logger;

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
        string key,
        int permitLimit,
        TimeSpan window,
        ILogger logger)
    {
        _redis = redis;
        _key = key;
        _permitLimit = permitLimit;
        _window = window;
        _logger = logger;
    }

    // Tell PartitionedRateLimiter to evict idle instances after the window expires,
    // preventing unbounded memory growth with large numbers of unique client IPs.
    public override TimeSpan? IdleDuration => _window;

    // Never touches Redis. ASP.NET Core's rate-limiting middleware tries this synchronous path first
    // and, when the lease isn't acquired, awaits AcquireAsync — so declining here sends every request
    // down the async path. Answering here meant a blocking Redis call holding a thread-pool thread
    // while the reply could only be processed on another one; with the pod's 1-core limit
    // (thread-pool minimum 1) that starved the pool and ~3.5 % of requests timed out and failed open
    // (28 Oct 2026: SE.Redis counters cur-in=4, WORKER Min=1, POOL queued 14, async-ops 0).
    protected override RateLimitLease AttemptAcquireCore(int permitCount) => DeferredLease.Instance;

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(
        int permitCount, CancellationToken cancellationToken)
    {
        // The only path that talks to Redis (see AttemptAcquireCore). No queuing —
        // acquire or reject immediately.
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
            LogFailOpen(ex);
            return SuccessfulLease.Instance;
        }
    }

    public override RateLimiterStatistics? GetStatistics()
    {
        try
        {
            RedisValue val = _redis.StringGet(_key);
            long current = val.HasValue && long.TryParse((string?)val, out long n) ? n : 0;
            long available = Math.Max(0, _permitLimit - current);
            return new RateLimiterStatistics
            {
                CurrentAvailablePermits = available,
                CurrentQueuedCount = 0,
                TotalSuccessfulLeases = 0,
                TotalFailedLeases = 0,
            };
        }
        catch
        {
            return null;
        }
    }

    protected override void Dispose(bool disposing) { /* stateless — nothing to release */ }

    private RateLimitLease EvaluateResult(RedisResult result)
    {
        RedisResult[] arr = (RedisResult[])result!;
        long count = (long)arr[0];
        long ttl = (long)arr[1]; // seconds remaining; -1 means no TTL (shouldn't happen)

        return count <= _permitLimit
            ? SuccessfulLease.Instance
            : new FailedLease(TimeSpan.FromSeconds(Math.Max(ttl, 1)));
    }

    // Logs only the exception types, the Redis failure kind and, for timeouts, an allow-list of
    // counters (RedisTimeoutDetail) — never the exception or its message: StackExchange.Redis
    // messages can include the key, and the key holds the client IP. The pod's own thread-pool
    // numbers go alongside so a starved pool can be told apart from a slow Redis.
    private void LogFailOpen(Exception ex)
    {
        string? failure = ex switch
        {
            RedisConnectionException connection => connection.FailureType.ToString(),
            RedisTimeoutException timeout => timeout.Commandstatus.ToString(),
            _ => null,
        };
        string? timeoutDetail = ex is RedisTimeoutException ? RedisTimeoutDetail.From(ex.Message) : null;
        ThreadPool.GetMinThreads(out int minWorkerThreads, out _);

        _logger.LogWarning(
            "Redis rate limiter unavailable — failing open ({RedisExceptionType}, failure {RedisFailure}, inner {RedisInnerExceptionType}, timeout detail {RedisTimeoutDetail}, thread pool {ThreadPoolThreads} threads / {ThreadPoolPending} queued / min {ThreadPoolMinWorkers})",
            ex.GetType().Name,
            failure,
            ex.InnerException?.GetType().Name,
            timeoutDetail,
            ThreadPool.ThreadCount,
            ThreadPool.PendingWorkItemCount,
            minWorkerThreads);
    }

    private static bool IsRedisException(Exception ex) =>
        ex is RedisConnectionException
           or RedisTimeoutException
           or RedisException;

    // "Not answered synchronously": not acquired and no RetryAfter, so the middleware moves on to
    // AcquireAsync instead of rejecting the request.
    private sealed class DeferredLease : RateLimitLease
    {
        public static readonly DeferredLease Instance = new();
        public override bool IsAcquired => false;
        public override IEnumerable<string> MetadataNames => [];
        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
        protected override void Dispose(bool disposing) { }
    }

    private sealed class SuccessfulLease : RateLimitLease
    {
        public static readonly SuccessfulLease Instance = new();
        public override bool IsAcquired => true;
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
        public override IEnumerable<string> MetadataNames => [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (metadataName == MetadataName.RetryAfter.Name)
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
