using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.API.Extensions;
using Pena_e_Arte.UnitTests.ConsentForms;
using StackExchange.Redis;
using System.Threading.RateLimiting;

namespace Pena_e_Arte.UnitTests.RateLimiting;

public class RedisFixedWindowRateLimiterTests
{
    private const int Limit = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private static RedisFixedWindowRateLimiter Create(IDatabase redis) =>
        new(redis, "rl:test:127.0.0.1", Limit, Window, NullLogger.Instance);

    private static IDatabase MockRedis(long count, long ttl = 30)
    {
        IDatabase db = Substitute.For<IDatabase>();
        RedisResult[] resultArray = [RedisResult.Create(count), RedisResult.Create(ttl)];
        RedisResult redisResult = RedisResult.Create(resultArray, ResultType.Array);

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

    [Fact]
    public async Task AcquireAsync_UnderLimit_AcquiresLease()
    {
        IDatabase db = MockRedis(count: 3, ttl: 45);
        RedisFixedWindowRateLimiter limiter = Create(db);

        RateLimitLease lease = await limiter.AcquireAsync(1);

        Assert.True(lease.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_AtLimit_AcquiresLease()
    {
        IDatabase db = MockRedis(count: Limit, ttl: 10);
        RedisFixedWindowRateLimiter limiter = Create(db);

        RateLimitLease lease = await limiter.AcquireAsync(1);

        Assert.True(lease.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_OverLimit_RejectsLease()
    {
        IDatabase db = MockRedis(count: Limit + 1, ttl: 20);
        RedisFixedWindowRateLimiter limiter = Create(db);

        RateLimitLease lease = await limiter.AcquireAsync(1);

        Assert.False(lease.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_OverLimit_SetsRetryAfter()
    {
        IDatabase db = MockRedis(count: Limit + 1, ttl: 42);
        RedisFixedWindowRateLimiter limiter = Create(db);

        RateLimitLease lease = await limiter.AcquireAsync(1);

        Assert.True(lease.TryGetMetadata(MetadataName.RetryAfter.Name, out object? meta));
        TimeSpan retryAfter = Assert.IsType<TimeSpan>(meta);
        Assert.Equal(42, (int)retryAfter.TotalSeconds);
    }

    [Fact]
    public async Task AcquireAsync_NegativeTtl_RetryAfterIsAtLeastOne()
    {
        IDatabase db = MockRedis(count: Limit + 1, ttl: -1);
        RedisFixedWindowRateLimiter limiter = Create(db);

        RateLimitLease lease = await limiter.AcquireAsync(1);

        Assert.False(lease.IsAcquired);
        Assert.True(lease.TryGetMetadata(MetadataName.RetryAfter.Name, out object? meta));
        TimeSpan retryAfter = Assert.IsType<TimeSpan>(meta);
        Assert.True(retryAfter.TotalSeconds >= 1);
    }

    [Fact]
    public async Task AcquireAsync_RedisUnavailable_FailsOpen()
    {
        IDatabase db = Substitute.For<IDatabase>();
        db.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns<Task<RedisResult>>(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "test"));

        RedisFixedWindowRateLimiter limiter = Create(db);

        RateLimitLease lease = await limiter.AcquireAsync(1);

        Assert.True(lease.IsAcquired, "Should fail open when Redis is unreachable");
    }

    [Fact]
    public async Task AcquireAsync_RedisTimeout_FailsOpen()
    {
        IDatabase db = Substitute.For<IDatabase>();
        db.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns<Task<RedisResult>>(_ => throw new RedisTimeoutException("test", CommandStatus.Unknown));

        RedisFixedWindowRateLimiter limiter = Create(db);

        RateLimitLease lease = await limiter.AcquireAsync(1);

        Assert.True(lease.IsAcquired, "Should fail open on Redis timeout");
    }

    [Fact]
    public async Task AcquireAsync_RedisTimeout_LogsExceptionTypeAndStatus_NotTheKey()
    {
        IDatabase db = Substitute.For<IDatabase>();
        db.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns<Task<RedisResult>>(_ => throw new RedisTimeoutException("Timeout key=rl:test:127.0.0.1", CommandStatus.Sent));
        CapturingLogger<RedisFixedWindowRateLimiterTests> logger = new();
        RedisFixedWindowRateLimiter limiter = new(db, "rl:test:127.0.0.1", Limit, Window, logger);

        await limiter.AcquireAsync(1);

        (LogLevel level, string message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("RedisTimeoutException", message);
        Assert.Contains("Sent", message);
        Assert.DoesNotContain("127.0.0.1", message);
    }

    // Shape of a StackExchange.Redis 3.x timeout message; the key and endpoint in it must never
    // reach the log, while the counters that tell a slow Redis from a starved pod must.
    private const string RealisticTimeoutMessage =
        "Timeout awaiting response (outbound=0KiB, inbound=0KiB, 1516ms elapsed, timeout is 1500ms), " +
        "command=EVAL, next: EVAL rl:public-write:203.0.113.7, inst: 0, qu: 0, qs: 1, aw: False, " +
        "bw: SpinningDown, rs: ReadAsync, ws: Idle, in: 12, in-pipe: 0, out-pipe: 0, last-in: 0, " +
        "cur-in: 0, sync-ops: 2, async-ops: 1234, serverEndpoint: tattooos-redis:6379, conn-sec: 4932.6, " +
        "aoc: 0, mc: 1/1/0, mgr: 10 of 10 available, clientName: tattooos-api-x(SE.Redis-v3.1.31), " +
        "IOCP: (Busy=0,Free=1000,Min=1,Max=1000), WORKER: (Busy=1,Free=32766,Min=1,Max=32767), " +
        "POOL: (Threads=6,QueuedItems=3,CompletedItems=12345,Timers=20), v: 3.1.31.37051";

    [Fact]
    public async Task AcquireAsync_RedisTimeout_LogsTimeoutCountersAndThreadPool_NotKeyOrEndpoint()
    {
        IDatabase db = Substitute.For<IDatabase>();
        db.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns<Task<RedisResult>>(_ => throw new RedisTimeoutException(RealisticTimeoutMessage, CommandStatus.Sent));
        CapturingLogger<RedisFixedWindowRateLimiterTests> logger = new();
        RedisFixedWindowRateLimiter limiter = new(db, "rl:public-write:203.0.113.7", Limit, Window, logger);

        await limiter.AcquireAsync(1);

        (_, string message) = Assert.Single(logger.Entries);
        Assert.Contains("elapsed=1516ms/1500ms", message);
        Assert.Contains("in=12", message);
        Assert.Contains("rs=ReadAsync", message);
        Assert.Contains("WORKER=(Busy=1,Free=32766,Min=1,Max=32767)", message);
        Assert.Contains("POOL=(Threads=6,QueuedItems=3,CompletedItems=12345,Timers=20)", message);
        Assert.Contains("thread pool", message);
        Assert.DoesNotContain("203.0.113.7", message);
        Assert.DoesNotContain("rl:public-write", message);
        Assert.DoesNotContain("tattooos-redis", message);
        Assert.DoesNotContain("clientName", message);
    }

    [Fact]
    public void RedisTimeoutDetail_KeepsOnlyAllowListedCounters()
    {
        string? detail = RedisTimeoutDetail.From(RealisticTimeoutMessage);

        Assert.Equal(
            "elapsed=1516ms/1500ms outbound=0KiB inbound=0KiB qu=0 qs=1 aw=False bw=SpinningDown rs=ReadAsync " +
            "ws=Idle in=12 in-pipe=0 out-pipe=0 last-in=0 cur-in=0 sync-ops=2 async-ops=1234 " +
            "IOCP=(Busy=0,Free=1000,Min=1,Max=1000) WORKER=(Busy=1,Free=32766,Min=1,Max=32767) " +
            "POOL=(Threads=6,QueuedItems=3,CompletedItems=12345,Timers=20)",
            detail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Timeout key=rl:test:127.0.0.1")]
    public void RedisTimeoutDetail_NoCounters_ReturnsNull(string? message)
    {
        Assert.Null(RedisTimeoutDetail.From(message));
    }

    [Fact]
    public async Task AcquireAsync_RedisUnavailable_LogsFailureType()
    {
        IDatabase db = Substitute.For<IDatabase>();
        db.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns<Task<RedisResult>>(_ => throw new RedisConnectionException(
                ConnectionFailureType.SocketFailure, "test", new IOException("reset")));
        CapturingLogger<RedisFixedWindowRateLimiterTests> logger = new();
        RedisFixedWindowRateLimiter limiter = new(db, "rl:test:127.0.0.1", Limit, Window, logger);

        await limiter.AcquireAsync(1);

        (_, string message) = Assert.Single(logger.Entries);
        Assert.Contains("RedisConnectionException", message);
        Assert.Contains("SocketFailure", message);
        Assert.Contains("IOException", message);
    }

    // The middleware tries AttemptAcquire first; it must never block on Redis (thread-pool starvation,
    // 28 Oct 2026). Declining with no RetryAfter makes the middleware await AcquireAsync instead.
    [Fact]
    public void AttemptAcquire_NeverCallsRedis_AndDefersToTheAsyncPath()
    {
        IDatabase db = MockRedis(count: 2, ttl: 55);
        RedisFixedWindowRateLimiter limiter = Create(db);

        RateLimitLease lease = limiter.AttemptAcquire(1);

        Assert.False(lease.IsAcquired);
        Assert.False(lease.TryGetMetadata(MetadataName.RetryAfter.Name, out _));
        Assert.Empty(db.ReceivedCalls());
    }

    [Fact]
    public void GetStatistics_ReturnsAvailablePermits()
    {
        IDatabase db = Substitute.For<IDatabase>();
        db.StringGet(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"3");

        RedisFixedWindowRateLimiter limiter = Create(db);

        RateLimiterStatistics? stats = limiter.GetStatistics();

        Assert.NotNull(stats);
        Assert.Equal(Limit - 3, stats!.CurrentAvailablePermits);
    }

    [Fact]
    public void GetStatistics_RedisUnavailable_ReturnsNull()
    {
        IDatabase db = Substitute.For<IDatabase>();
        db.StringGet(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "test"));

        RedisFixedWindowRateLimiter limiter = Create(db);

        RateLimiterStatistics? stats = limiter.GetStatistics();

        Assert.Null(stats);
    }

    [Fact]
    public void IdleDuration_EqualsConfiguredWindow()
    {
        IDatabase db = MockRedis(1, 60);
        RedisFixedWindowRateLimiter limiter = Create(db);

        Assert.Equal(Window, limiter.IdleDuration);
    }

    [Fact]
    public async Task SuccessfulLease_HasNoMetadata()
    {
        IDatabase db = MockRedis(count: 1, ttl: 60);
        RedisFixedWindowRateLimiter limiter = Create(db);

        RateLimitLease lease = await limiter.AcquireAsync(1);

        Assert.True(lease.IsAcquired);
        Assert.Empty(lease.MetadataNames);
        Assert.False(lease.TryGetMetadata(MetadataName.RetryAfter.Name, out _));
    }

    [Fact]
    public async Task FailedLease_MetadataNames_ContainsRetryAfter()
    {
        IDatabase db = MockRedis(count: Limit + 1, ttl: 10);
        RedisFixedWindowRateLimiter limiter = Create(db);

        RateLimitLease lease = await limiter.AcquireAsync(1);

        Assert.False(lease.IsAcquired);
        Assert.Single(lease.MetadataNames, MetadataName.RetryAfter.Name);
        Assert.False(lease.TryGetMetadata("unknown-key", out _));
    }
}
