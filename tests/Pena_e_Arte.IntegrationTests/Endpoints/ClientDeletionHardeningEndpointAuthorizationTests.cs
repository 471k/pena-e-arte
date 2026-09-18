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
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Endpoints;

// Exercises the four new/changed client-deletion-hardening endpoints (archive/restore,
// cancel-erasure, me/export) through the REAL ASP.NET Core authorization pipeline, mirroring
// ClientArtistEndpointAuthorizationTests's approach — a fake in-memory DbContext doesn't register
// query filters at all, so it cannot prove the multi-studio erasure fan-out actually crosses the
// tenant boundary correctly (see §9 of the client-account-deletion-hardening prompt).
[Collection("Database")]
public class ClientDeletionHardeningEndpointAuthorizationTests(DatabaseFixture fixture)
{
    private const string SigningKeyValue = "client-deletion-hardening-endpoint-test-key-32b!";

    [Fact]
    public async Task PostArchive_OwnerToken_Returns204()
    {
        Guid tenantId = Guid.NewGuid();
        Guid clientId = await SeedClientAsync(tenantId);

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(tenantId, "owner");

        HttpResponseMessage response = await PostAsync(client, token, $"/api/v1/clients/{clientId}/archive");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task PostArchive_ClientToken_Returns403()
    {
        Guid tenantId = Guid.NewGuid();
        Guid clientId = await SeedClientAsync(tenantId);

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(tenantId, "client");

        HttpResponseMessage response = await PostAsync(client, token, $"/api/v1/clients/{clientId}/archive");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostRestore_ArtistToken_Returns204()
    {
        Guid tenantId = Guid.NewGuid();
        Guid clientId = await SeedClientAsync(tenantId, archived: true);

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(tenantId, "artist");

        HttpResponseMessage response = await PostAsync(client, token, $"/api/v1/clients/{clientId}/restore");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task PostCancelErasure_OwnerToken_Returns204()
    {
        Guid tenantId = Guid.NewGuid();
        Guid clientId = await SeedClientAsync(tenantId, erasureRequested: true);

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(tenantId, "owner");

        HttpResponseMessage response = await PostAsync(client, token, $"/api/v1/clients/{clientId}/cancel-erasure");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task PostCancelErasure_ArtistToken_Returns403()
    {
        Guid tenantId = Guid.NewGuid();
        Guid clientId = await SeedClientAsync(tenantId, erasureRequested: true);

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(tenantId, "artist");

        HttpResponseMessage response = await PostAsync(client, token, $"/api/v1/clients/{clientId}/cancel-erasure");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetMyExport_NoToken_Returns401()
    {
        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/clients/me/export");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMyExport_ClientToken_ReturnsOnlyCallersOwnData()
    {
        Guid tenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        await SeedClientForUserAsync(tenantId, userId, "mine@example.com");

        Guid otherTenantId = Guid.NewGuid();
        await SeedClientForUserAsync(otherTenantId, Guid.NewGuid(), "other@example.com");

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(tenantId, "client", userId);

        HttpRequestMessage request = new(HttpMethod.Get, "/api/v1/clients/me/export");
        request.Headers.Add("Authorization", $"Bearer {token}");
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ClientDataExportResponse? body = await response.Content.ReadFromJsonAsync<ClientDataExportResponse>();
        body!.Studios.Should().ContainSingle();
        body.Studios[0].Profile.Email.Should().Be("mine@example.com");
    }

    private async Task<Guid> SeedClientAsync(
        Guid tenantId, bool archived = false, bool erasureRequested = false)
    {
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        Client client = new()
        {
            StudioId = tenantId,
            FirstName = "Test",
            LastName = "Client",
            Email = $"{Guid.NewGuid()}@test.com",
            ArchivedAt = archived ? DateTime.UtcNow : null,
            ErasureRequestedAt = erasureRequested ? DateTime.UtcNow : null,
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client.Id;
    }

    private async Task SeedClientForUserAsync(Guid tenantId, Guid userId, string email)
    {
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        Client client = new()
        {
            StudioId = tenantId,
            UserId = userId,
            FirstName = "Test",
            LastName = "Client",
            Email = email,
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string token, string path)
    {
        HttpRequestMessage request = new(HttpMethod.Post, path);
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await client.SendAsync(request);
    }

    private async Task<IHost> BuildHost()
    {
        ISubscriptionAccessService subscriptions = Substitute.For<ISubscriptionAccessService>();
        subscriptions.IsStudioActiveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        subscriptions.GetSnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionSnapshot(SubscriptionStatus.Active, null, DateTime.MinValue));
        IPlanLimitService planLimits = Substitute.For<IPlanLimitService>();
        IIdentityService identity = Substitute.For<IIdentityService>();
        IR2Service r2 = Substitute.For<IR2Service>();

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
                    services.AddSingleton(planLimits);
                    services.AddSingleton(identity);
                    services.AddSingleton(r2);

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseMySql(fixture.ConnectionString, new MySqlServerVersion(new Version(8, 0, 0))));
                    services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

                    Assembly applicationAssembly = typeof(ValidationBehavior<,>).Assembly;
                    services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(applicationAssembly));
                    services.AddValidatorsFromAssembly(applicationAssembly);
                    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
                    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PlanLimitBehavior<,>));
                    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditLogBehavior<,>));

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
                    app.UseEndpoints(endpoints => endpoints.MapClientEndpoints());
                });
            });

        return await builder.StartAsync();
    }

    private static string BuildToken(Guid tenantId, string role, Guid? userId = null) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "iss", audience: "aud",
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, (userId ?? Guid.NewGuid()).ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim("tenant_id", tenantId.ToString()),
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKeyValue)),
                SecurityAlgorithms.HmacSha256)));
}
