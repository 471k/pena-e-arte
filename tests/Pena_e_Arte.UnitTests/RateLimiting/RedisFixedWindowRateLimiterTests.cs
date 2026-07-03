using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.API.Extensions;
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
    public void AttemptAcquire_UnderLimit_AcquiresLease()
    {
        IDatabase db = MockRedis(count: 2, ttl: 55);
        RedisFixedWindowRateLimiter limiter = Create(db);

        RateLimitLease lease = limiter.AttemptAcquire(1);

        Assert.True(lease.IsAcquired);
    }

    [Fact]
    public void AttemptAcquire_OverLimit_RejectsLease()
    {
        IDatabase db = MockRedis(count: Limit + 3, ttl: 15);
        RedisFixedWindowRateLimiter limiter = Create(db);

        RateLimitLease lease = limiter.AttemptAcquire(1);

        Assert.False(lease.IsAcquired);
    }

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

        RedisFixedWindowRateLimiter limiter = Create(db);

        RateLimitLease lease = limiter.AttemptAcquire(1);

        Assert.True(lease.IsAcquired, "Should fail open on Redis error");
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
