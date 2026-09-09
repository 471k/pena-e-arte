using System.IdentityModel.Tokens.Jwt;
using System.Net;
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
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Endpoints;

// Exercises GET /api/v1/clients/export.csv through the REAL ASP.NET Core authorization pipeline
// (AddApiAuthorization()'s "OwnerOnly" policy + app.UseAuthorization()) end-to-end, and confirms
// the new literal "/export.csv" route doesn't collide with the pre-existing "{clientId:guid}"
// route it sits alongside in the same MapGroup — a GUID-shaped id must still resolve to
// GetClientById, not swallowed by the new sibling route. Representative for the other two CSV
// export endpoints (appointments, revenue) added in the same change — those are covered by
// unit-level route-metadata tests (see AppointmentEndpointsAuthorizationTests,
// ReportEndpointsAuthorizationTests) plus handler-level unit tests.
[Collection("Database")]
public class ClientExportCsvEndpointAuthorizationTests(DatabaseFixture fixture)
{
    private const string SigningKeyValue = "client-export-csv-endpoint-test-key-32-bytes!";

    [Fact]
    public async Task ExportClientsCsv_OwnerToken_Returns200WithCsvBody()
    {
        Guid tenantId = Guid.NewGuid();
        await SeedClientAsync(tenantId, "Jane", "Doe");

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(tenantId, "owner");

        HttpResponseMessage response = await Send(client, token, "/api/v1/clients/export.csv");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Jane").And.Contain("Doe");
    }

    [Fact]
    public async Task ExportClientsCsv_ArtistToken_Returns403()
    {
        Guid tenantId = Guid.NewGuid();
        await SeedClientAsync(tenantId, "Jane", "Doe");

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(tenantId, "artist");

        HttpResponseMessage response = await Send(client, token, "/api/v1/clients/export.csv");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetClientById_RealGuidRoute_StillResolvesAlongsideExportCsvRoute()
    {
        Guid tenantId = Guid.NewGuid();
        Guid clientId = await SeedClientAsync(tenantId, "Jane", "Doe");

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(tenantId, "owner");

        HttpResponseMessage response = await Send(client, token, $"/api/v1/clients/{clientId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Jane").And.Contain("Doe");
    }

    private async Task<Guid> SeedClientAsync(Guid tenantId, string firstName, string lastName)
    {
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        Client client = new()
        {
            StudioId = tenantId,
            FirstName = firstName,
            LastName = lastName,
            Email = $"{Guid.NewGuid()}@test.com",
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client.Id;
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string token, string path)
    {
        HttpRequestMessage request = new(HttpMethod.Get, path);
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
