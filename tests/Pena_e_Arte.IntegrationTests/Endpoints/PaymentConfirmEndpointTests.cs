using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
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

// POST /api/v1/payments/{id}/confirm (C08) through the real authorization pipeline ("ClientAndAbove"),
// MediatR + validators and the real DbContext. /payment-result, a public route, now calls this, so this
// pins that the endpoint itself still refuses anyone but the payment's own client and that what it
// returns carries no client identity or studio free text.
[Collection("Database")]
public class PaymentConfirmEndpointTests(DatabaseFixture fixture)
{
    private const string SigningKeyValue = "payment-confirm-endpoint-test-key-32b!!!";

    [Fact]
    public async Task NoToken_Returns401()
    {
        Seeded s = await SeedAsync();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await client.PostAsync($"/api/v1/payments/{s.PaymentId}/confirm", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnotherClientOfTheSameStudio_IsRefused()
    {
        Seeded s = await SeedAsync();
        Guid otherUserId = Guid.NewGuid();
        await using (AppDbContext ctx = fixture.CreateDbContext(s.TenantId))
        {
            ctx.Clients.Add(new Client { StudioId = s.TenantId, UserId = otherUserId, FirstName = "O", LastName = "T", Email = $"{Guid.NewGuid():N}@o.test" });
            await ctx.SaveChangesAsync();
        }
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await Post(client, BuildToken(s.TenantId, "client", otherUserId), $"/api/v1/payments/{s.PaymentId}/confirm");

        // The handler's UnauthorizedAccessException; ExceptionMiddleware maps it to 401.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("\"status\":\"Paid\"").And.NotContain(s.PaymentId.ToString());
    }

    [Fact]
    public async Task AClientOfAnotherStudio_GetsNotFound()
    {
        Seeded s = await SeedAsync();
        Seeded other = await SeedAsync();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        // The tenant filter hides the row before the ownership check is reached.
        HttpResponseMessage response = await Post(client, BuildToken(other.TenantId, "client", other.ClientUserId), $"/api/v1/payments/{s.PaymentId}/confirm");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OwningClient_GetsTheNarrowResponse_WithNoIdentityOrCashNote()
    {
        Seeded s = await SeedAsync();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await Post(client, BuildToken(s.TenantId, "client", s.ClientUserId), $"/api/v1/payments/{s.PaymentId}/confirm");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("\"status\":\"Paid\"").And.Contain("\"appointmentCancelled\":false");
        // The seeded values, not just the member names: nothing identifying reaches the page.
        json.Should().NotContain("Zana").And.NotContain("tok_secret").And.NotContain("studio-only note")
            .And.NotContain("order-ref").And.NotContain("clientName").And.NotContain("splits");
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string token, string url)
    {
        HttpRequestMessage request = new(HttpMethod.Post, url);
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await client.SendAsync(request);
    }

    private sealed record Seeded(Guid TenantId, Guid PaymentId, Guid ClientUserId);

    private async Task<Seeded> SeedAsync()
    {
        Guid tenantId = Guid.NewGuid();
        Guid clientUserId = Guid.NewGuid();
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        ctx.Studios.Add(new Studio { Id = tenantId, Name = "Mine", Slug = tenantId.ToString("N")[..8], CountryCode = "PT", Currency = "EUR" });
        Artist artist = new() { StudioId = tenantId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid():N}@a.test" };
        Client client = new() { StudioId = tenantId, UserId = clientUserId, FirstName = "Zana", LastName = "Kola", Email = $"{Guid.NewGuid():N}@c.test" };
        ctx.Artists.Add(artist);
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();
        DateTime start = DateTime.UtcNow.AddDays(7);
        Appointment appointment = new()
        {
            StudioId = tenantId,
            ArtistId = artist.Id,
            ClientId = client.Id,
            Date = start,
            EndDate = start.AddHours(1),
            DurationMinutes = 60,
            Status = AppointmentStatus.Confirmed,
            DepositStatus = DepositStatus.Paid,
            DepositAmount = 50m,
        };
        ctx.Appointments.Add(appointment);
        await ctx.SaveChangesAsync();
        // Already Paid: the handler answers without a provider call.
        Payment payment = new()
        {
            StudioId = tenantId,
            AppointmentId = appointment.Id,
            ClientId = client.Id,
            Amount = 50m,
            Currency = "EUR",
            Status = PaymentStatus.Paid,
            Method = ClientPaymentMethod.Card,
            ProviderReferenceId = "order-ref",
            ClientToken = "tok_secret",
            CashNote = "studio-only note",
            PaidAt = DateTime.UtcNow,
        };
        ctx.Payments.Add(payment);
        await ctx.SaveChangesAsync();
        return new Seeded(tenantId, payment.Id, clientUserId);
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
                    services.AddSingleton(Substitute.For<IPlanLimitService>());
                    services.AddSingleton(Substitute.For<IJobScheduler>());
                    services.AddSingleton(Substitute.For<IPaymentProvider>());

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseMySql(fixture.ConnectionString, DatabaseServerVersion.MySql));
                    services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

                    Assembly applicationAssembly = typeof(ValidationBehavior<,>).Assembly;
                    services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(applicationAssembly));
                    services.AddValidatorsFromAssembly(applicationAssembly);
                    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
                    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PlanLimitBehavior<,>));
                    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditLogBehavior<,>));

                    // The real "billing" policy needs Redis (BillingRateLimitingTests covers it); this
                    // test is about authorization and the response shape, so the policy is a no-op here.
                    services.AddRateLimiter(o => o.AddPolicy("billing",
                        _ => RateLimitPartition.GetNoLimiter("test")));

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
                    app.UseRateLimiter();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapPaymentEndpoints());
                });
            });

        return await builder.StartAsync();
    }

    private static string BuildToken(Guid tenantId, string role, Guid userId) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "iss", audience: "aud",
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim("tenant_id", tenantId.ToString()),
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKeyValue)),
                SecurityAlgorithms.HmacSha256)));
}
