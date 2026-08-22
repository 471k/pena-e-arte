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
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Application.Common.Behaviors;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Endpoints;

// Exercises PATCH /api/v1/clients/{clientId}/artist through the REAL ASP.NET Core authorization
// pipeline (AddApiAuthorization()'s "OwnerOnly" policy + app.UseAuthorization()) end-to-end,
// rather than only asserting route metadata (see ClientEndpointsAuthorizationTests) or calling
// the handler directly (see ClientHandlerIntegrationTests). The whole point is to catch a future
// regression where "OwnerOnly" is silently weakened (e.g. a role-claim mapping change) that a
// metadata-only or handler-only test cannot see, since both bypass the real middleware pipeline.
[Collection("Database")]
public class ClientArtistEndpointAuthorizationTests(DatabaseFixture fixture)
{
    private const string SigningKeyValue = "client-artist-endpoint-test-key-32-bytes-min!";

    [Fact]
    public async Task PatchClientArtist_OwnerToken_Returns200()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid clientId, Guid artistId) = await SeedClientAndArtistAsync(tenantId);

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(tenantId, "owner");

        HttpResponseMessage response = await Send(client, token, clientId, artistId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ClientResponse? body = await response.Content.ReadFromJsonAsync<ClientResponse>();
        body!.ArtistId.Should().Be(artistId);
    }

    [Fact]
    public async Task PatchClientArtist_ArtistToken_Returns403()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid clientId, Guid artistId) = await SeedClientAndArtistAsync(tenantId);

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(tenantId, "artist");

        HttpResponseMessage response = await Send(client, token, clientId, artistId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PatchClientArtist_NoToken_Returns401()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid clientId, Guid artistId) = await SeedClientAndArtistAsync(tenantId);

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();

        HttpRequestMessage request = new(HttpMethod.Patch, $"/api/v1/clients/{clientId}/artist")
        {
            Content = JsonContent.Create(new UpdateClientArtistRequest(artistId)),
        };
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<(Guid clientId, Guid artistId)> SeedClientAndArtistAsync(Guid tenantId)
    {
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        Client client = new()
        {
            StudioId = tenantId,
            FirstName = "Test",
            LastName = "Client",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        Artist artist = new()
        {
            StudioId = tenantId,
            FirstName = "Test",
            LastName = "Artist",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        db.Clients.Add(client);
        db.Artists.Add(artist);
        await db.SaveChangesAsync();
        return (client.Id, artist.Id);
    }

    private static async Task<HttpResponseMessage> Send(
        HttpClient client, string token, Guid clientId, Guid artistId)
    {
        HttpRequestMessage request = new(HttpMethod.Patch, $"/api/v1/clients/{clientId}/artist")
        {
            Content = JsonContent.Create(new UpdateClientArtistRequest(artistId)),
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
        IPlanLimitService planLimits = Substitute.For<IPlanLimitService>();

        IHostBuilder builder = new HostBuilder()
            // AddMediatR scans the whole Application assembly, registering every handler in it —
            // most need infrastructure (email, Stripe, Redis, ...) this narrow test host doesn't
            // provide. Only the ClientEndpoints/UpdateClientArtist path is actually exercised, so
            // validate-on-build (which eagerly resolves every registered service's constructor
            // graph, not just the ones used) is disabled rather than stubbing dozens of unrelated
            // services this test has no interest in.
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
