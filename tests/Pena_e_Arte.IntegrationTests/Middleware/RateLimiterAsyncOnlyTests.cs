using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NSubstitute.Core;
using Pena_e_Arte.API.Extensions;
using StackExchange.Redis;

namespace Pena_e_Arte.IntegrationTests.Middleware;

// The rate-limiting middleware tries the synchronous AttemptAcquire first. A blocking Redis call
// there held a thread-pool thread while the reply could only be processed on another one; with the
// pod's 1-core limit (thread-pool minimum 1) that starved the pool and ~3.5 % of production beacons
// timed out and failed open (28 Oct 2026: SE.Redis counters cur-in=4, WORKER Min=1, POOL queued 14,
// sync-ops rising, async-ops 0). Runs the real AddApiRateLimiting() + UseRateLimiter() pipeline.
public class RateLimiterAsyncOnlyTests
{
    private const int PublicWritePermitLimit = 30;

    [Fact]
    public async Task Requests_NeverCallRedisSynchronously()
    {
        (IHost host, IDatabase db) = await BuildHost();
        using (host)
        {
            HttpClient client = host.GetTestServer().CreateClient();

            for (int i = 0; i < 5; i++)
                (await client.PostAsync("/beacon-probe", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

            db.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IDatabase.ScriptEvaluate))
                .Should().BeEmpty("a blocking Redis call on a thread-pool thread starves the pool");
            db.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IDatabase.ScriptEvaluateAsync))
                .Should().Be(5);
        }
    }

    [Fact]
    public async Task OverTheLimit_StillReturnsTooManyRequests_ThroughTheAsyncPath()
    {
        (IHost host, _) = await BuildHost();
        using (host)
        {
            HttpClient client = host.GetTestServer().CreateClient();

            List<HttpStatusCode> codes = [];
            for (int i = 0; i < PublicWritePermitLimit + 1; i++)
                codes.Add((await client.PostAsync("/beacon-probe", null)).StatusCode);

            codes.Take(PublicWritePermitLimit).Should().AllSatisfy(c => c.Should().Be(HttpStatusCode.NoContent));
            codes[PublicWritePermitLimit].Should().Be(HttpStatusCode.TooManyRequests);
        }
    }

    private static async Task<(IHost Host, IDatabase Db)> BuildHost()
    {
        IDatabase db = FakeCountingRedis();
        IConnectionMultiplexer redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).ReturnsForAnyArgs(db);

        IHost host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(redis);
                    services.AddApiRateLimiting();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseEndpoints(endpoints =>
                        endpoints.MapPost("/beacon-probe", () => Results.NoContent())
                            .RequireRateLimiting("public-write"));
                });
            })
            .StartAsync();
        return (host, db);
    }

    private static IDatabase FakeCountingRedis()
    {
        Dictionary<string, long> counts = [];

        RedisResult Increment(CallInfo callInfo)
        {
            string key = callInfo.ArgAt<RedisKey[]>(1)[0].ToString()!;
            long count = counts.TryGetValue(key, out long existing) ? existing + 1 : 1;
            counts[key] = count;
            return RedisResult.Create([RedisResult.Create(count), RedisResult.Create(60L)], ResultType.Array);
        }

        IDatabase db = Substitute.For<IDatabase>();
        db.ScriptEvaluate(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
            .Returns(Increment);
        db.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
            .Returns(callInfo => Task.FromResult(Increment(callInfo)));
        return db;
    }
}
