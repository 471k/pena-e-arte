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
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Endpoints;

// Full Support Impersonation session lifecycle through the REAL ASP.NET Core pipeline —
// authentication, AddApiAuthorization() policies, and TenantMiddleware's impersonation
// gate all running together. IIdentityService is substituted (real ASP.NET Core Identity
// user management is out of scope for this test) but reproduces the exact claim shape
// IdentityService.GenerateJwt would produce, using the arguments the handler actually
// passes — see BuildImpersonationToken.
//
// Deliberately weights negative cases (denied scope, denied write, post-End rejection) at
// least as heavily as the positive allow-listed path — a false negative here (an
// impersonation token reaching something it shouldn't) is the actual security bug the
// 2026-09-09 P1 backlog audit warned about, not a UX inconvenience.
[Collection("Database")]
public class SupportImpersonationEndpointTests(DatabaseFixture fixture)
{
    private const string SigningKeyValue = "impersonation-endpoint-test-signing-key-32b!";

    [Fact]
    public async Task StartImpersonation_ValidStudio_Returns200WithTokenAndSession()
    {
        Guid studioId = await SeedStudioAsync("Ink & Iron");
        Guid adminUserId = Guid.NewGuid();

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();

        HttpResponseMessage response = await StartImpersonation(client, adminUserId, studioId, "Investigating ticket #123");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ImpersonationTokenResponse? body = await response.Content.ReadFromJsonAsync<ImpersonationTokenResponse>();
        body.Should().NotBeNull();
        body!.StudioId.Should().Be(studioId);
        body.StudioName.Should().Be("Ink & Iron");
        body.AccessToken.Should().NotBeNullOrWhiteSpace();

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        ImpersonationSession session = await db.ImpersonationSessions.SingleAsync(s => s.Id == body.SessionId);
        session.ActorUserId.Should().Be(adminUserId);
        session.StudioId.Should().Be(studioId);
        session.EndedAt.Should().BeNull();
        session.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(45), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ImpersonationToken_AllowListedGetRoute_Returns200()
    {
        Guid studioId = await SeedStudioAsync("Allow-listed Studio");
        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();

        (string impersonationToken, _) = await StartAndExtract(client, studioId);

        HttpResponseMessage response = await Get(client, impersonationToken, "/api/v1/appointments");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ImpersonationToken_NonAllowListedGetRoute_Returns403WithScopeDeniedCode()
    {
        Guid studioId = await SeedStudioAsync("Deny-listed Studio");
        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();

        (string impersonationToken, _) = await StartAndExtract(client, studioId);

        // "/api/v1/artists/me" — ArtistAndAbove policy admits the "admin" role, so RBAC alone
        // would let this through; the allow-list only matches a GUID id segment, not "me",
        // so the gate must be the thing that blocks it.
        HttpResponseMessage response = await Get(client, impersonationToken, "/api/v1/artists/me");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("IMPERSONATION_SCOPE_DENIED");
    }

    [Fact]
    public async Task ImpersonationToken_WriteToAllowListedResource_Returns403()
    {
        Guid studioId = await SeedStudioAsync("Write-denied Studio");
        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();

        (string impersonationToken, _) = await StartAndExtract(client, studioId);

        HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/appointments")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Add("Authorization", $"Bearer {impersonationToken}");
        HttpResponseMessage response = await client.SendAsync(request);

        // Either the impersonation gate's own 403, or a validation 422 for the empty body —
        // both prove the write never reached CreateAppointmentHandler. The gate runs in
        // TenantMiddleware, strictly before model binding/validation, so 403 is expected.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EndImpersonationSession_ThenReuseToken_Returns403NotJustScopeLimited()
    {
        Guid studioId = await SeedStudioAsync("End-session Studio");
        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();

        (string impersonationToken, Guid sessionId) = await StartAndExtract(client, studioId);

        // Sanity: the token works before ending.
        (await Get(client, impersonationToken, "/api/v1/appointments")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        HttpRequestMessage endRequest = new(HttpMethod.Post, $"/api/v1/platform/impersonation-sessions/{sessionId}/end");
        endRequest.Headers.Add("Authorization", $"Bearer {BuildAdminToken(Guid.NewGuid())}");
        HttpResponseMessage endResponse = await client.SendAsync(endRequest);
        endResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        ImpersonationSession session = await db.ImpersonationSessions.SingleAsync(s => s.Id == sessionId);
        session.EndedAt.Should().NotBeNull();

        // Same token, now past EndedAt — an allow-listed route must still reject it, not
        // just succeed because the route itself is fine. Proves EndImpersonationSessionCommand
        // takes effect immediately rather than waiting for the JWT's own "exp" to pass.
        HttpResponseMessage reuse = await Get(client, impersonationToken, "/api/v1/appointments");
        reuse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ImpersonationToken_ExpiredSession_Returns403()
    {
        Guid studioId = await SeedStudioAsync("Expired Studio");
        Guid sessionId = Guid.NewGuid();
        Guid adminUserId = Guid.NewGuid();

        await using (AppDbContext seedDb = fixture.CreateDbContext(studioId))
        {
            // Bypass StartImpersonationCommand entirely — seed an already-expired session
            // directly, the same shape a real 45-minutes-ago session would have.
            seedDb.Database.ExecuteSqlInterpolated($"""
                INSERT INTO ImpersonationSessions
                    (Id, StudioId, ActorUserId, ReasonCode, ExpiresAt, EndedAt, CreatedAt, UpdatedAt, DeletedAt)
                VALUES
                    ({sessionId}, {studioId}, {adminUserId}, {"expired-session-fixture"},
                     {DateTime.UtcNow.AddMinutes(-1)}, {(DateTime?)null},
                     {DateTime.UtcNow.AddMinutes(-46)}, {DateTime.UtcNow.AddMinutes(-46)}, {(DateTime?)null})
                """);
        }

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string expiredToken = BuildImpersonationToken(adminUserId, studioId, sessionId, DateTime.UtcNow.AddMinutes(30));

        HttpResponseMessage response = await Get(client, expiredToken, "/api/v1/appointments");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private async Task<Guid> SeedStudioAsync(string name)
    {
        Guid studioId = Guid.NewGuid();
        await using AppDbContext db = fixture.CreateDbContext(studioId);
        db.Studios.Add(new Studio
        {
            Id = studioId,
            Name = name,
            Slug = $"{name.ToLowerInvariant().Replace(" ", "-").Replace("&", "and")}-{studioId:N}",
        });
        await db.SaveChangesAsync();
        return studioId;
    }

    private async Task<(string ImpersonationToken, Guid SessionId)> StartAndExtract(HttpClient client, Guid studioId)
    {
        HttpResponseMessage response = await StartImpersonation(client, Guid.NewGuid(), studioId, "Diagnosing a booking issue");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ImpersonationTokenResponse body = (await response.Content.ReadFromJsonAsync<ImpersonationTokenResponse>())!;
        return (body.AccessToken, body.SessionId);
    }

    private static async Task<HttpResponseMessage> StartImpersonation(
        HttpClient client, Guid adminUserId, Guid studioId, string reason)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/api/v1/platform/studios/{studioId}/impersonate")
        {
            Content = JsonContent.Create(new StartImpersonationRequest(reason)),
        };
        request.Headers.Add("Authorization", $"Bearer {BuildAdminToken(adminUserId)}");
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> Get(HttpClient client, string token, string path)
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

        IIdentityService identity = Substitute.For<IIdentityService>();
        identity
            .IssueImpersonationTokenAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>())
            .Returns(callInfo =>
            {
                Guid adminUserId = callInfo.ArgAt<Guid>(0);
                Guid targetStudioId = callInfo.ArgAt<Guid>(1);
                Guid sessionId = callInfo.ArgAt<Guid>(2);
                DateTime expiresAt = callInfo.ArgAt<DateTime>(3);
                string token = BuildImpersonationToken(adminUserId, targetStudioId, sessionId, expiresAt);
                return Task.FromResult<(bool, string?, string?)>((true, token, null));
            });

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
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapPlatformEndpoints();
                        endpoints.MapAppointmentEndpoints();
                        endpoints.MapArtistEndpoints();
                    });
                });
            });

        return await builder.StartAsync();
    }

    // The admin's own token — no tenant_id claim, matching a real admin account, which has
    // none unless it holds an active impersonation session (see AdminBootstrapper).
    private static string BuildAdminToken(Guid adminUserId) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "iss", audience: "aud",
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, adminUserId.ToString()),
                new Claim(ClaimTypes.Role, "admin"),
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKeyValue)),
                SecurityAlgorithms.HmacSha256)));

    // Reproduces the exact claim shape IdentityService.GenerateJwt/IssueImpersonationTokenAsync
    // would produce for a real impersonation token: role stays "admin", tenant_id is the
    // TARGET studio, "imp" carries the session id, Sub/NameIdentifier stays the real admin's
    // own id (never a synthetic identity).
    private static string BuildImpersonationToken(Guid adminUserId, Guid studioId, Guid sessionId, DateTime expiresAt) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "iss", audience: "aud",
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, adminUserId.ToString()),
                new Claim(ClaimTypes.Role, "admin"),
                new Claim("tenant_id", studioId.ToString()),
                new Claim("imp", sessionId.ToString()),
            ],
            expires: expiresAt,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKeyValue)),
                SecurityAlgorithms.HmacSha256)));
}
