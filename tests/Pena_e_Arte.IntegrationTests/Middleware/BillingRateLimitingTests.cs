using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using NSubstitute.Core;
using Pena_e_Arte.API.Extensions;
using StackExchange.Redis;

namespace Pena_e_Arte.IntegrationTests.Middleware;

// Exercises the real AddApiRateLimiting() wiring end-to-end (Redis mocked with a stateful
// in-memory counter, matching RedisFixedWindowRateLimiterTests' mocking technique) rather than
// just asserting route metadata, since the whole point of Finding 7 is the runtime 429 behavior
// and the per-user partition key — which depends on Program.cs's UseAuthentication/UseRateLimiter
// ordering actually being correct (see the comment on that reordering in Program.cs).
public class BillingRateLimitingTests
{
    private const string SigningKeyValue = "billing-rate-limit-test-key-32-bytes-min!!";
    private const int BillingPermitLimit = 20;

    [Fact]
    public async Task ExceedingBillingLimit_ReturnsTooManyRequests()
    {
        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(Guid.NewGuid());

        List<HttpStatusCode> statusCodes = [];
        for (int i = 0; i < BillingPermitLimit + 1; i++)
            statusCodes.Add((await Send(client, token)).StatusCode);

        statusCodes.Take(BillingPermitLimit).Should().AllSatisfy(
            code => code.Should().Be(HttpStatusCode.OK));
        statusCodes[BillingPermitLimit].Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task LegitimateSequenceUnderLimit_AllSucceed()
    {
        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(Guid.NewGuid());

        List<HttpStatusCode> statusCodes = [];
        for (int i = 0; i < 5; i++)
            statusCodes.Add((await Send(client, token)).StatusCode);

        statusCodes.Should().AllSatisfy(code => code.Should().Be(HttpStatusCode.OK));
    }

    [Fact]
    public async Task DifferentAuthenticatedUsers_DoNotShareABucket()
    {
        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string tokenA = BuildToken(Guid.NewGuid());
        string tokenB = BuildToken(Guid.NewGuid());

        for (int i = 0; i < BillingPermitLimit; i++)
            (await Send(client, tokenA)).StatusCode.Should().Be(HttpStatusCode.OK);
        HttpResponseMessage userAOverLimit = await Send(client, tokenA);
        HttpResponseMessage userBFirstRequest = await Send(client, tokenB);

        userAOverLimit.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        userBFirstRequest.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<IHost> BuildHost()
    {
        IDatabase fakeDb = FakeCountingRedis();
        IConnectionMultiplexer redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).ReturnsForAnyArgs(fakeDb);

        IHostBuilder builder = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(redis);
                    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                        .AddJwtBearer(o =>
                        {
                            o.TokenValidationParameters = new TokenValidationParameters
                            {
                                ValidIssuer = "iss",
                                ValidAudience = "aud",
                                IssuerSigningKey = new SymmetricSecurityKey(
                                    Encoding.UTF8.GetBytes(SigningKeyValue)),
                                ClockSkew = TimeSpan.Zero,
                            };
                        });
                    services.AddAuthorization();
                    services.AddApiRateLimiting();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseRateLimiter();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                        endpoints.MapGet("/billing-probe", () => Results.Ok())
                            .RequireAuthorization()
                            .RequireRateLimiting("billing"));
                });
            });

        return await builder.StartAsync();
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string token)
    {
        HttpRequestMessage request = new(HttpMethod.Get, "/billing-probe");
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await client.SendAsync(request);
    }

    private static string BuildToken(Guid userId) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "iss", audience: "aud",
            claims: [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKeyValue)),
                SecurityAlgorithms.HmacSha256)));

    private static IDatabase FakeCountingRedis()
    {
        Dictionary<string, long> counts = [];

        RedisResult Increment(CallInfo callInfo)
        {
            RedisKey[] keys = callInfo.ArgAt<RedisKey[]>(1);
            string key = keys[0].ToString()!;
            long count = counts.TryGetValue(key, out long existing) ? existing + 1 : 1;
            counts[key] = count;
            RedisResult[] resultArray = [RedisResult.Create(count), RedisResult.Create(60L)];
            return RedisResult.Create(resultArray, ResultType.Array);
        }

        IDatabase db = Substitute.For<IDatabase>();
        // The rate-limiting middleware calls the synchronous acquire path
        // (RedisFixedWindowRateLimiter.AttemptAcquireCore) when it can — both must be mocked.
        db.ScriptEvaluate(
                Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
            .Returns(Increment);
        db.ScriptEvaluateAsync(
                Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
            .Returns(callInfo => Task.FromResult(Increment(callInfo)));
        return db;
    }
}
