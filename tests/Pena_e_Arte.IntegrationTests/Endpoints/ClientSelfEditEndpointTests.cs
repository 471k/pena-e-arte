using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Endpoints;

// PATCH /api/v1/clients/me (client self-service name/phone edit, PR #163) through the REAL
// ASP.NET Core pipeline against REAL MySQL. The unit tests use a fake in-memory DbContext, which
// has no query filters, so they cannot prove the multi-studio fan-out: Client is one row per
// studio, the caller's JWT is scoped to ONE studio, and the other studios' rows are only reachable
// through IgnoreQueryFilters (FindAllClientRecordsForUserAsync).
[Collection("Database")]
public class ClientSelfEditEndpointTests(DatabaseFixture fixture)
{
    private const string SigningKeyValue = "client-self-edit-endpoint-test-key-32-bytes!!";

    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();

    [Fact]
    public async Task Patch_UserWithClientRowsInTwoStudios_UpdatesBothRowsAndNoOthers()
    {
        Guid userId = Guid.NewGuid();
        Guid studioA = Guid.NewGuid();
        Guid studioB = Guid.NewGuid();
        Guid rowA = await SeedClientAsync(studioA, userId, "Ana", "Costa", "+351912000001");
        Guid rowB = await SeedClientAsync(studioB, userId, "Ana", "Costa", "+351912000001");
        Guid bystanderSameStudio = await SeedClientAsync(studioA, Guid.NewGuid(), "Bea", "Lima", "+351912000002");
        Guid bystanderOtherStudio = await SeedClientAsync(studioB, Guid.NewGuid(), "Rui", "Neves", "+351912000003");

        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await PatchAsync(
            client, BuildToken(studioA, userId), new UpdateMyClientRequest("Anita", "Costa-Silva", "+351912999999"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetClientAsync(rowA)).Should().BeEquivalentTo(new { FirstName = "Anita", LastName = "Costa-Silva", Phone = "+351912999999" },
            o => o.ExcludingMissingMembers());
        (await GetClientAsync(rowB)).Should().BeEquivalentTo(new { FirstName = "Anita", LastName = "Costa-Silva", Phone = "+351912999999" },
            o => o.ExcludingMissingMembers(),
            because: "the edit must reach the caller's client row in every other studio, not only the one in their JWT");
        await _identity.Received(1).SetUserGivenNameAsync(userId, "Anita", Arg.Any<CancellationToken>());
        (await GetClientAsync(bystanderSameStudio)).FirstName.Should().Be("Bea");
        (await GetClientAsync(bystanderOtherStudio)).FirstName.Should().Be("Rui");
    }

    [Fact]
    public async Task Patch_BlankPhone_ClearsThePhoneOnEveryRow()
    {
        Guid userId = Guid.NewGuid();
        Guid studioA = Guid.NewGuid();
        Guid studioB = Guid.NewGuid();
        Guid rowA = await SeedClientAsync(studioA, userId, "Ana", "Costa", "+351912000001");
        Guid rowB = await SeedClientAsync(studioB, userId, "Ana", "Costa", "+351912000001");

        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await PatchAsync(
            client, BuildToken(studioA, userId), new UpdateMyClientRequest("Ana", "Costa", "  "));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetClientAsync(rowA)).Phone.Should().BeNull();
        (await GetClientAsync(rowB)).Phone.Should().BeNull();
    }

    [Fact]
    public async Task Patch_NamesAreTrimmed()
    {
        Guid userId = Guid.NewGuid();
        Guid studioA = Guid.NewGuid();
        Guid rowA = await SeedClientAsync(studioA, userId, "Ana", "Costa", null);

        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        await PatchAsync(client, BuildToken(studioA, userId), new UpdateMyClientRequest("  Ana  ", " Costa ", null));

        Client row = await GetClientAsync(rowA);
        row.FirstName.Should().Be("Ana");
        row.LastName.Should().Be("Costa");
    }

    [Fact]
    public async Task Patch_InvalidPhone_Returns422AndChangesNothing()
    {
        Guid userId = Guid.NewGuid();
        Guid studioA = Guid.NewGuid();
        Guid studioB = Guid.NewGuid();
        Guid rowA = await SeedClientAsync(studioA, userId, "Ana", "Costa", "+351912000001");
        Guid rowB = await SeedClientAsync(studioB, userId, "Ana", "Costa", "+351912000001");

        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await PatchAsync(
            client, BuildToken(studioA, userId), new UpdateMyClientRequest("Changed", "Name", "12"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await GetClientAsync(rowA)).FirstName.Should().Be("Ana");
        (await GetClientAsync(rowB)).FirstName.Should().Be("Ana");
    }

    [Fact]
    public async Task Patch_NoToken_Returns401()
    {
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            "/api/v1/clients/me", new UpdateMyClientRequest("Ana", "Costa", null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Patch_UserWithNoClientRecord_Returns404()
    {
        Guid studioA = Guid.NewGuid();

        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await PatchAsync(
            client, BuildToken(studioA, Guid.NewGuid()), new UpdateMyClientRequest("Ana", "Costa", null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Patch_WritesOneAuditRowWithTheAffectedCountAndNoPersonalData()
    {
        Guid userId = Guid.NewGuid();
        Guid studioA = Guid.NewGuid();
        Guid studioB = Guid.NewGuid();
        await SeedClientAsync(studioA, userId, "Ana", "Costa", "+351912000001");
        await SeedClientAsync(studioB, userId, "Ana", "Costa", "+351912000001");

        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        await PatchAsync(client, BuildToken(studioA, userId), new UpdateMyClientRequest("Uniquefirst", "Uniquelast", "+351912888888"));

        await using AppDbContext db = fixture.CreateDbContext(studioA);
        List<AuditLogEntry> rows = await db.AuditLogEntries
            .Where(a => a.ActorUserId == userId && a.Action == "Client.SelfProfileUpdated")
            .ToListAsync();

        AuditLogEntry row = rows.Should().ContainSingle().Subject;
        // MySQL's JSON column normalises whitespace, so parse instead of matching the raw text.
        JsonDocument.Parse(row.Metadata).RootElement.GetProperty("affectedClientCount").GetInt32().Should().Be(2);
        row.Metadata.Should().NotContain("Uniquefirst").And.NotContain("Uniquelast").And.NotContain("912888888");
    }

    private async Task<Guid> SeedClientAsync(Guid studioId, Guid userId, string first, string last, string? phone)
    {
        await using AppDbContext db = fixture.CreateDbContext(studioId);
        Client client = new()
        {
            StudioId = studioId,
            UserId = userId,
            FirstName = first,
            LastName = last,
            Email = $"{Guid.NewGuid()}@test.com",
            Phone = phone,
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client.Id;
    }

    // IgnoreQueryFilters: the assertion must see every studio's row, whichever tenant it belongs to.
    private async Task<Client> GetClientAsync(Guid id)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.NewGuid());
        return await db.Clients.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == id);
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, string token, UpdateMyClientRequest body)
    {
        HttpRequestMessage request = new(HttpMethod.Patch, "/api/v1/clients/me")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private async Task<IHost> BuildHost()
    {
        ISubscriptionAccessService subscriptions = Substitute.For<ISubscriptionAccessService>();
        subscriptions.IsStudioActiveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        subscriptions.GetSnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionSnapshot(SubscriptionStatus.Active, null, DateTime.MinValue));
        IPlanLimitService planLimits = Substitute.For<IPlanLimitService>();
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
                    services.AddSingleton(_identity);
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

    private static string BuildToken(Guid tenantId, Guid userId) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "iss", audience: "aud",
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, "client"),
                new Claim("tenant_id", tenantId.ToString()),
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKeyValue)),
                SecurityAlgorithms.HmacSha256)));
}
