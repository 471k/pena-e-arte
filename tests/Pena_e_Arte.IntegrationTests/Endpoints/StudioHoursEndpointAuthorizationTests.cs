using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Pena_e_Arte.API.Endpoints;
using Pena_e_Arte.API.Extensions;
using Pena_e_Arte.API.Middleware;
using Pena_e_Arte.Application.Common.Behaviors;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Studios.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Endpoints;

// Exercises GET/PUT /api/v1/studios/{id}/hours through the REAL ASP.NET Core authorization
// pipeline — mirrors AppointmentArtistEndpointAuthorizationTests' precedent (a metadata-only
// or handler-only test cannot see a future regression where a policy is silently weakened).
[Collection("Database")]
public class StudioHoursEndpointAuthorizationTests(DatabaseFixture fixture)
{
    private const string SigningKeyValue = "studio-hours-endpoint-test-key-32-bytes!!";

    [Fact]
    public async Task GetHours_ClientToken_Returns200()
    {
        Guid tenantId = Guid.NewGuid();
        await SeedStudio(tenantId);

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(tenantId, "client");

        HttpRequestMessage request = new(HttpMethod.Get, $"/api/v1/studios/{tenantId}/hours");
        request.Headers.Add("Authorization", $"Bearer {token}");
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHours_NoToken_Returns401()
    {
        Guid tenantId = Guid.NewGuid();
        await SeedStudio(tenantId);

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();

        HttpResponseMessage response = await client.GetAsync($"/api/v1/studios/{tenantId}/hours");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PutHours_OwnerToken_Returns204AndPersists()
    {
        Guid tenantId = Guid.NewGuid();
        await SeedStudio(tenantId);

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(tenantId, "owner");

        HttpResponseMessage response = await SendUpsert(client, token, tenantId);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        List<StudioHours> rows = verify.StudioHours.Where(h => h.StudioId == tenantId).ToList();
        rows.Should().ContainSingle(h => h.DayOfWeek == DayOfWeek.Monday);
    }

    [Fact]
    public async Task PutHours_ClientToken_Returns403()
    {
        Guid tenantId = Guid.NewGuid();
        await SeedStudio(tenantId);

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(tenantId, "client");

        HttpResponseMessage response = await SendUpsert(client, token, tenantId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PutHours_ArtistToken_Returns403()
    {
        Guid tenantId = Guid.NewGuid();
        await SeedStudio(tenantId);

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(tenantId, "artist");

        HttpResponseMessage response = await SendUpsert(client, token, tenantId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task SeedStudio(Guid tenantId)
    {
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        db.Studios.Add(new Studio { Id = tenantId, Name = "Hours Test Studio", Slug = $"hours-test-{tenantId}", City = "Porto" });
        await db.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> SendUpsert(HttpClient client, string token, Guid tenantId)
    {
        UpsertStudioHoursRequest body = new(
        [
            new StudioHoursEntryRequest(DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(18), true),
        ]);
        HttpRequestMessage request = new(HttpMethod.Put, $"/api/v1/studios/{tenantId}/hours")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await client.SendAsync(request);
    }

    private async Task<IHost> BuildHost()
    {
        ISubscriptionAccessService subscriptions = Substitute.For<ISubscriptionAccessService>();
        subscriptions.IsStudioActiveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        subscriptions.GetSnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionSnapshot(SubscriptionStatus.Active, null, DateTime.MinValue));

        IHostBuilder builder = new HostBuilder()
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = false;
                options.ValidateScopes = false;
            })
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddHttpContextAccessor();
                    services.AddScoped<ICurrentTenant, CurrentTenantService>();
                    services.AddScoped<ICurrentUser, CurrentUserService>();
                    services.AddSingleton(subscriptions);

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseMySql(fixture.ConnectionString, new MySqlServerVersion(new Version(8, 0, 0))));
                    services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

                    Assembly applicationAssembly = typeof(ValidationBehavior<,>).Assembly;
                    services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(applicationAssembly));
                    services.AddValidatorsFromAssembly(applicationAssembly);
                    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

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
                    services.AddApiAuthorization();
                });
                webBuilder.Configure(app =>
                {
                    app.UseMiddleware<ExceptionMiddleware>();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseMiddleware<TenantMiddleware>();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapStudioEndpoints());
                });
            });

        return await builder.StartAsync();
    }

    private static string BuildToken(Guid tenantId, string role) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "iss", audience: "aud",
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim("tenant_id", tenantId.ToString()),
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKeyValue)),
                SecurityAlgorithms.HmacSha256)));
}
