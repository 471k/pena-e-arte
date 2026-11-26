using System.Diagnostics;
using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Infrastructure.Services;
using StackExchange.Redis;

namespace Pena_e_Arte.UnitTests.Services;

/// <summary>The SignalR Redis backplane waits for the connection before subscribing (pod-start race).</summary>
public class RedisConnectionWaitTests
{
    [Fact]
    public async Task ReturnsAtOnce_WhenAlreadyConnected()
    {
        IConnectionMultiplexer connection = Substitute.For<IConnectionMultiplexer>();
        connection.IsConnected.Returns(true);
        Stopwatch sw = Stopwatch.StartNew();

        await RedisConnectionWait.WaitUntilConnectedAsync(connection, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50));

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task WaitsUntilTheConnectionComesUp()
    {
        IConnectionMultiplexer connection = Substitute.For<IConnectionMultiplexer>();
        connection.IsConnected.Returns(false, false, false, true);

        await RedisConnectionWait.WaitUntilConnectedAsync(connection, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10));

        _ = connection.Received(4).IsConnected;
    }

    /// <summary>
    /// Gives up at the maximum, asserted as a poll count, not as elapsed time: an elapsed-time bound on a shared
    /// runner measures the runner (2.68 s against a 2 s cap on 24 Nov skipped a deploy). The clock steps 20 ms per
    /// read, so with a 200 ms maximum the wait sees 20, 40 ... 200 ms elapsed and checks the connection exactly ten
    /// times: one fewer means it gave up early, more means the deadline arithmetic drifted, and a wait that never
    /// gave up would hang until the 10 s guard fails it.
    /// </summary>
    [Fact]
    public async Task GivesUpAfterTheMaximum_WhenRedisStaysDown()
    {
        IConnectionMultiplexer connection = Substitute.For<IConnectionMultiplexer>();
        connection.IsConnected.Returns(false);
        SteppingMonotonicClock clock = new(stepPerRead: TimeSpan.FromMilliseconds(20));

        await RedisConnectionWait.WaitUntilConnectedAsync(
                connection, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(1), CancellationToken.None, clock)
            .WaitAsync(TimeSpan.FromSeconds(10));

        _ = connection.Received(10).IsConnected;
    }

    /// <summary>
    /// SMOKE CHECK on the real clock, kept deliberately loose. The poll-count test above proves the deadline; this
    /// one only catches a real regression the stepping clock cannot see, such as a wait that ignores its maximum.
    /// The 2.68 s seen on 24 Nov was a real stall on a busy runner, which is why the bound is 10 s, not 2 s: a
    /// failure here is a hang worth reading, not a slow runner.
    /// </summary>
    [Fact]
    public async Task SmokeCheck_GivesUpOnTheRealClock_WellWithinTenSeconds()
    {
        IConnectionMultiplexer connection = Substitute.For<IConnectionMultiplexer>();
        connection.IsConnected.Returns(false);
        Stopwatch sw = Stopwatch.StartNew();

        await RedisConnectionWait.WaitUntilConnectedAsync(connection, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(20));

        // The lower bound is safe on any runner: the wait itself measures at least 200 ms on the monotonic clock.
        sw.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(180))
            .And.BeLessThan(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// The give-up deadline is measured on the monotonic clock, not the wall clock. The provider below has a
    /// wall clock that throws if read and a monotonic clock that jumps one minute per read, so a 5-minute wait
    /// ends after about five polls. Both regressions fail here: deriving the deadline from GetUtcNow() throws,
    /// and going back to DateTime.UtcNow ignores the provider and waits 5 real minutes, which the 10 s guard
    /// turns into a failure instead of a hung run.
    /// </summary>
    [Fact]
    public async Task MeasuresTheWaitOnTheMonotonicClock_NotTheWallClock()
    {
        IConnectionMultiplexer connection = Substitute.For<IConnectionMultiplexer>();
        connection.IsConnected.Returns(false);
        SteppingMonotonicClock clock = new(stepPerRead: TimeSpan.FromMinutes(1));

        await RedisConnectionWait.WaitUntilConnectedAsync(
                connection, TimeSpan.FromMinutes(5), TimeSpan.FromMilliseconds(5), CancellationToken.None, clock)
            .WaitAsync(TimeSpan.FromSeconds(10));

        _ = connection.Received(5).IsConnected;
    }

    private sealed class SteppingMonotonicClock(TimeSpan stepPerRead) : TimeProvider
    {
        private long _timestamp;

        public override DateTimeOffset GetUtcNow() =>
            throw new InvalidOperationException("A duration must not be measured on the wall clock.");

        public override long GetTimestamp() =>
            _timestamp += (long)(stepPerRead.TotalSeconds * TimestampFrequency);
    }
}
