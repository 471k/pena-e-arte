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

    [Fact]
    public async Task GivesUpAfterTheMaximum_WhenRedisStaysDown()
    {
        IConnectionMultiplexer connection = Substitute.For<IConnectionMultiplexer>();
        connection.IsConnected.Returns(false);
        Stopwatch sw = Stopwatch.StartNew();

        await RedisConnectionWait.WaitUntilConnectedAsync(connection, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(20));

        sw.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(180))
            .And.BeLessThan(TimeSpan.FromSeconds(2));
    }
}
