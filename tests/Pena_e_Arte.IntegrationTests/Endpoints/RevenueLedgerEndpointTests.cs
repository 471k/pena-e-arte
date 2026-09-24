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

// The revenue-ledger admin endpoints through the REAL ASP.NET Core pipeline — the real MediatR
// container (so every new handler/validator is actually resolved, which FakeDbContext unit tests
// bypass), real authentication + the AdminOnly policy, and real MySQL (so the migration, the
// unique StripeEventId index and the FK are all exercised, not just the in-memory provider).
[Collection("Database")]
public class RevenueLedgerEndpointTests(DatabaseFixture fixture)
{
    private const string SigningKeyValue = "revenue-ledger-endpoint-test-signing-key-32b!";

    [Theory]
    [InlineData("GET", "/api/v1/platform/mrr-history")]
    [InlineData("GET", "/api/v1/platform/mrr-movements")]
    [InlineData("GET", "/api/v1/platform/revenue-retention")]
    [InlineData("POST", "/api/v1/platform/subscriptions/backfill-revenue-ledger")]
    public async Task LedgerEndpoints_WithoutAToken_Return401(string method, string path)
    {
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("GET", "/api/v1/platform/mrr-movements")]
    [InlineData("GET", "/api/v1/platform/revenue-retention")]
    [InlineData("POST", "/api/v1/platform/subscriptions/backfill-revenue-ledger")]
    public async Task LedgerEndpoints_WithANonAdminToken_Return403(string method, string path)
    {
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await client.SendAsync(WithToken(method, path, BuildToken("owner")));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Backfill_ThenReads_SeedsALedgerRowAndTheChartsSeeIt_AndRerunIsANoOp()
    {
        Guid subscriptionId = await SeedActivePaidSubscriptionAsync(billedAmount: 59m);

        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();
        string admin = BuildToken("admin");

        // ── first backfill run seeds our subscription
        HttpResponseMessage first = await client.SendAsync(
            WithToken("POST", "/api/v1/platform/subscriptions/backfill-revenue-ledger", admin));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        BackfillRevenueLedgerResponse firstBody = (await first.Content.ReadFromJsonAsync<BackfillRevenueLedgerResponse>())!;
        firstBody.Created.Should().BeGreaterThanOrEqualTo(1);

        await using (AppDbContext db = fixture.CreateDbContext(Guid.Empty))
        {
            SubscriptionRevenueEvent row = await db.SubscriptionRevenueEvents.SingleAsync(e => e.SubscriptionId == subscriptionId);
            row.Type.Should().Be(RevenueEventType.New);
            row.MrrBefore.Should().Be(0m);
            row.MrrAfter.Should().Be(59m);
            row.Source.Should().Be("Backfill");
            row.StripeEventId.Should().NotBeNullOrWhiteSpace();
        }

        // ── rerun writes nothing new for our subscription
        HttpResponseMessage second = await client.SendAsync(
            WithToken("POST", "/api/v1/platform/subscriptions/backfill-revenue-ledger", admin));
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        await using (AppDbContext db = fixture.CreateDbContext(Guid.Empty))
        {
            (await db.SubscriptionRevenueEvents.CountAsync(e => e.SubscriptionId == subscriptionId)).Should().Be(1);
        }

        // ── the MRR trend now reads the ledger for the current month
        HttpResponseMessage history = await client.SendAsync(WithToken("GET", "/api/v1/platform/mrr-history?months=3", admin));
        history.StatusCode.Should().Be(HttpStatusCode.OK);
        List<MrrDataPointResponse> points = (await history.Content.ReadFromJsonAsync<List<MrrDataPointResponse>>())!;
        points.Should().HaveCount(3);
        points[^1].IsEstimated.Should().BeFalse();
        points[^1].Mrr.Should().BeGreaterThanOrEqualTo(59m);

        // ── movements + retention resolve through the real container and serialize
        HttpResponseMessage movements = await client.SendAsync(WithToken("GET", "/api/v1/platform/mrr-movements?months=3", admin));
        movements.StatusCode.Should().Be(HttpStatusCode.OK);
        (await movements.Content.ReadFromJsonAsync<List<MrrMovementsDataPointResponse>>())!.Should().HaveCount(3);

        HttpResponseMessage retention = await client.SendAsync(WithToken("GET", "/api/v1/platform/revenue-retention", admin));
        retention.StatusCode.Should().Be(HttpStatusCode.OK);
        (await retention.Content.ReadFromJsonAsync<RevenueRetentionResponse>()).Should().NotBeNull();
    }

    [Fact]
    public async Task Ledger_RejectsADuplicateStripeEventId_AtTheDatabaseLevel()
    {
        Guid subscriptionId = await SeedActivePaidSubscriptionAsync(billedAmount: 29m);
        string eventId = $"evt_{Guid.NewGuid():N}";

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        Subscription sub = await db.Subscriptions.SingleAsync(s => s.Id == subscriptionId);

        db.SubscriptionRevenueEvents.Add(NewEvent(sub, eventId));
        await db.SaveChangesAsync();

        await using AppDbContext second = fixture.CreateDbContext(Guid.Empty);
        second.SubscriptionRevenueEvents.Add(NewEvent(sub, eventId));

        Func<Task> act = () => second.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private static SubscriptionRevenueEvent NewEvent(Subscription sub, string stripeEventId) => new()
    {
        SubscriptionId = sub.Id,
        StudioId = sub.StudioId,
        OccurredAt = DateTime.UtcNow,
        Type = RevenueEventType.PastDue,
        MrrBefore = 29m,
        MrrAfter = 29m,
        Source = "test",
        StripeEventId = stripeEventId,
    };

    private async Task<Guid> SeedActivePaidSubscriptionAsync(decimal billedAmount)
    {
        Guid studioId = Guid.NewGuid();
        await using AppDbContext db = fixture.CreateDbContext(studioId);

        db.Studios.Add(new Studio
        {
            Id = studioId,
            Name = $"Ledger {studioId:N}"[..20],
            Slug = $"ledger-{studioId:N}"[..28],
            IsActive = true,
            TrialExpiresAt = DateTime.UtcNow.AddDays(-60),
        });
        Plan plan = new() { Name = $"LedgerPlan-{Guid.NewGuid():N}" };
        db.Plans.Add(plan);
        await db.SaveChangesAsync();

        Subscription subscription = new()
        {
            StudioId = studioId,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            BillingInterval = BillingInterval.Monthly,
            BilledUnitAmount = billedAmount,
            BilledQuantity = 1,
            BilledCurrency = "eur",
            CreatedAt = DateTime.UtcNow.AddDays(-45),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(15),
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return subscription.Id;
    }

    private static HttpRequestMessage WithToken(string method, string path, string token)
    {
        HttpRequestMessage request = new(new HttpMethod(method), path);
        request.Headers.Add("Authorization", $"Bearer {token}");
        return request;
    }

    private async Task<IHost> BuildHost()
    {
        ISubscriptionAccessService subscriptions = Substitute.For<ISubscriptionAccessService>();
        subscriptions.IsStudioActiveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        subscriptions.GetSnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionSnapshot(SubscriptionStatus.Active, null, DateTime.MinValue));
        IPlanLimitService planLimits = Substitute.For<IPlanLimitService>();
        IIdentityService identity = Substitute.For<IIdentityService>();

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
                                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKeyValue)),
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
                    app.UseEndpoints(endpoints => endpoints.MapPlatformEndpoints());
                });
            });

        return await builder.StartAsync();
    }

    // Admin/owner token with no tenant_id claim, matching a real platform admin account.
    private static string BuildToken(string role) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "iss", audience: "aud",
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, role),
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKeyValue)),
                SecurityAlgorithms.HmacSha256)));
}
