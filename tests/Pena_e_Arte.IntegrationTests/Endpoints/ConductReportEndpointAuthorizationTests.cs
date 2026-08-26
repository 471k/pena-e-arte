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
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Endpoints;

// Exercises the ConductReport read/status endpoints through the REAL ASP.NET Core
// authorization pipeline + real MediatR/AuditLogBehavior pipeline, end-to-end — the redaction
// guarantee in particular must hold over the actual HTTP JSON response, not just in a handler
// called directly in isolation (see GetMyConductReportsAsArtistHandlerTests for the
// handler-level version of this same assertion).
[Collection("Database")]
public class ConductReportEndpointAuthorizationTests(DatabaseFixture fixture)
{
    private const string SigningKeyValue = "conduct-report-endpoint-test-key-32-bytes-min!";

    [Fact]
    public async Task ArtistRead_RealHttpResponse_NeverContainsReporterIdentity()
    {
        Guid studioId = Guid.NewGuid();
        Guid artistUserId = Guid.NewGuid();
        (Guid artistId, _) = await SeedArtistAndReportAsync(studioId, artistUserId, ReportCategory.Harassment);

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(studioId, artistUserId, "artist");

        HttpRequestMessage request = new(HttpMethod.Get, "/api/v1/artists/me/conduct-reports");
        request.Headers.Add("Authorization", $"Bearer {token}");
        HttpResponseMessage response = await client.SendAsync(request);
        string rawJson = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, rawJson);
        rawJson.Should().NotContain("reporterUserId\":\"");
        rawJson.Should().NotContain("Jane Real Reporter");

        List<ConductReportResponse>? body = await response.Content.ReadFromJsonAsync<List<ConductReportResponse>>();
        body.Should().ContainSingle();
        body![0].ArtistId.Should().Be(artistId);
        body[0].ReporterUserId.Should().BeNull();
        body[0].ReporterName.Should().BeNull();
    }

    [Fact]
    public async Task OwnerRead_SeesFullReporterIdentity()
    {
        Guid studioId = Guid.NewGuid();
        (_, Guid reportId) = await SeedArtistAndReportAsync(studioId, Guid.NewGuid(), ReportCategory.PoorServiceQuality);

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(studioId, Guid.NewGuid(), "owner");

        HttpRequestMessage request = new(HttpMethod.Get, "/api/v1/studios/me/conduct-reports");
        request.Headers.Add("Authorization", $"Bearer {token}");
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        List<ConductReportResponse>? body = await response.Content.ReadFromJsonAsync<List<ConductReportResponse>>();
        body.Should().ContainSingle(r => r.Id == reportId);
        body![0].ReporterName.Should().Be("Jane Real Reporter");
    }

    [Fact]
    public async Task UpdateStatus_OwnerOnHighSeverity_Returns403_IssuerCanThenResolve_AndAuditRowIsWritten()
    {
        Guid studioId = Guid.NewGuid();
        (_, Guid reportId) = await SeedArtistAndReportAsync(studioId, Guid.NewGuid(), ReportCategory.SexualMisconduct);

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient ownerClient = server.CreateClient();
        string ownerToken = BuildToken(studioId, Guid.NewGuid(), "owner");

        HttpResponseMessage ownerAttempt = await PatchStatus(ownerClient, ownerToken, reportId, "Resolved");
        ownerAttempt.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using HttpClient issuerClient = server.CreateClient();
        string issuerToken = BuildToken(null, Guid.NewGuid(), "issuer");

        HttpResponseMessage issuerAttempt = await PatchStatus(issuerClient, issuerToken, reportId, "Resolved");
        issuerAttempt.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        ConductReport updated = await db.ConductReports.SingleAsync(r => r.Id == reportId);
        updated.Status.Should().Be(ReportStatus.Resolved);

        AuditLogEntry? auditRow = await db.AuditLogEntries
            .FirstOrDefaultAsync(a => a.Action == AuditActions.ConductReportStatusUpdated && a.TargetId == reportId);
        auditRow.Should().NotBeNull();
    }

    private async Task<(Guid artistId, Guid reportId)> SeedArtistAndReportAsync(
        Guid studioId, Guid artistUserId, ReportCategory category)
    {
        await using AppDbContext db = fixture.CreateDbContext(studioId);
        Studio studio = new()
        {
            Id = studioId,
            Name = "Ink Studio",
            Slug = $"studio-{studioId}",
            City = "Lisbon",
            IsActive = true,
            OwnerEmail = "owner@ink-studio.test",
        };
        Artist artist = new()
        {
            StudioId = studioId,
            UserId = artistUserId,
            FirstName = "Maria",
            LastName = "Silva",
            Email = "maria@example.com",
        };
        db.Studios.Add(studio);
        db.Artists.Add(artist);
        await db.SaveChangesAsync();

        ConductReport report = ConductReport.ForArtist(
            studioId, artist.Id, Guid.NewGuid(), Guid.NewGuid(), "Jane Real Reporter",
            category, "A detailed report body describing the incident in the reporter's own words.");
        db.ConductReports.Add(report);
        await db.SaveChangesAsync();

        return (artist.Id, report.Id);
    }

    private static async Task<HttpResponseMessage> PatchStatus(
        HttpClient client, string token, Guid reportId, string status)
    {
        HttpRequestMessage request = new(HttpMethod.Patch, $"/api/v1/conduct-reports/{reportId}/status")
        {
            Content = JsonContent.Create(new UpdateConductReportStatusRequest(status, null)),
        };
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await client.SendAsync(request);
    }

    private async Task<IHost> BuildHost()
    {
        // TenantMiddleware resolves ISubscriptionAccessService to check trial/suspension state
        // for every authenticated request — this narrow test host has no real Stripe/DB-backed
        // implementation wired, so it's stubbed the same way ClientArtistEndpointAuthorizationTests
        // stubs it.
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
                    app.UseEndpoints(endpoints => endpoints.MapConductReportEndpoints());
                });
            });

        return await builder.StartAsync();
    }

    private static string BuildToken(Guid? tenantId, Guid userId, string role)
    {
        List<Claim> claims =
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role),
        ];
        if (tenantId is Guid tid) claims.Add(new Claim("tenant_id", tid.ToString()));

        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "iss", audience: "aud",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKeyValue)),
                SecurityAlgorithms.HmacSha256)));
    }
}
