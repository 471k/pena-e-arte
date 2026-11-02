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

// GET /api/v1/appointments/{id}/cancellation-preview through the real authorization pipeline
// ("ClientAndAbove"), MediatR + validator scanning and the real DbContext: the route is mapped and
// protected, a client sees only their own appointment (404, never 403), and staff get the
// no-notice-window projection.
[Collection("Database")]
public class CancellationPreviewEndpointTests(DatabaseFixture fixture)
{
    private const string SigningKeyValue = "cancellation-preview-endpoint-test-key-32b!";

    [Fact]
    public async Task NoToken_Returns401()
    {
        (_, Guid appointmentId, _) = await SeedAsync();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await client.GetAsync($"/api/v1/appointments/{appointmentId}/cancellation-preview");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OwningClient_Returns200WithTheProjection()
    {
        (Guid tenantId, Guid appointmentId, Guid clientUserId) = await SeedAsync();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await Get(client, BuildToken(tenantId, "client", clientUserId), appointmentId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        CancellationPreviewResponse? body = await response.Content.ReadFromJsonAsync<CancellationPreviewResponse>();
        body!.AppointmentId.Should().Be(appointmentId);
        body.NoticeWindowApplies.Should().BeTrue();
        body.IsWithinNoticeWindow.Should().BeTrue();
        body.Outcome.Should().Be("FullyRefunded");
        body.RefundAmount.Should().Be(50m);
        body.IsProjection.Should().BeTrue();
    }

    [Fact]
    public async Task AnotherClient_Returns404_Not403()
    {
        (Guid tenantId, Guid appointmentId, _) = await SeedAsync();
        Guid otherUserId = Guid.NewGuid();
        await using (AppDbContext ctx = fixture.CreateDbContext(tenantId))
        {
            ctx.Clients.Add(new Client { StudioId = tenantId, UserId = otherUserId, FirstName = "O", LastName = "T", Email = $"{Guid.NewGuid():N}@o.test" });
            await ctx.SaveChangesAsync();
        }
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await Get(client, BuildToken(tenantId, "client", otherUserId), appointmentId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Owner_Returns200_WithoutTheNoticeWindow()
    {
        (Guid tenantId, Guid appointmentId, _) = await SeedAsync();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await Get(client, BuildToken(tenantId, "owner", Guid.NewGuid()), appointmentId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        CancellationPreviewResponse? body = await response.Content.ReadFromJsonAsync<CancellationPreviewResponse>();
        body!.NoticeWindowApplies.Should().BeFalse();
        body.RefundPercent.Should().Be(100);
    }

    private static async Task<HttpResponseMessage> Get(HttpClient client, string token, Guid appointmentId)
    {
        HttpRequestMessage request = new(HttpMethod.Get, $"/api/v1/appointments/{appointmentId}/cancellation-preview");
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await client.SendAsync(request);
    }

    private async Task<(Guid TenantId, Guid AppointmentId, Guid ClientUserId)> SeedAsync()
    {
        Guid tenantId = Guid.NewGuid();
        Guid clientUserId = Guid.NewGuid();
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        ctx.Studios.Add(new Studio { Id = tenantId, Name = "Preview", Slug = tenantId.ToString("N")[..8], CountryCode = "PT", Currency = "EUR" });
        Artist artist = new() { StudioId = tenantId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid():N}@a.test" };
        Client client = new() { StudioId = tenantId, UserId = clientUserId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid():N}@c.test" };
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
        ctx.Payments.Add(new Payment
        {
            StudioId = tenantId,
            AppointmentId = appointment.Id,
            ClientId = client.Id,
            Amount = 50m,
            Currency = "EUR",
            Status = PaymentStatus.Paid,
            Method = ClientPaymentMethod.Card,
            ProviderReferenceId = "pok_order_1",
        });
        await ctx.SaveChangesAsync();
        return (tenantId, appointment.Id, clientUserId);
    }

    private async Task<IHost> BuildHost()
    {
        ISubscriptionAccessService subscriptions = Substitute.For<ISubscriptionAccessService>();
        subscriptions.IsStudioActiveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        subscriptions.GetSnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionSnapshot(SubscriptionStatus.Active, null, DateTime.MinValue));
        IPlanLimitService planLimits = Substitute.For<IPlanLimitService>();
        ISlotLocker slotLocker = Substitute.For<ISlotLocker>();
        slotLocker.TryAcquireLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);
        IRealtimeNotifier realtime = Substitute.For<IRealtimeNotifier>();
        IJobScheduler jobs = Substitute.For<IJobScheduler>();
        jobs.ScheduleAppointmentReminder(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns("hangfire-job-1");
        IEmailRenderer emailRenderer = Substitute.For<IEmailRenderer>();
        INotificationService notifications = Substitute.For<INotificationService>();
        INotificationPreferenceService prefs = Substitute.For<INotificationPreferenceService>();
        prefs.IsEnabledAsync(Arg.Any<Guid>(), Arg.Any<NotificationType>(), Arg.Any<NotificationChannel>(), Arg.Any<CancellationToken>())
            .Returns(false);
        IR2Service r2 = Substitute.For<IR2Service>();
        r2.IsR2Url(Arg.Any<string>()).Returns(true);

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
                    services.AddSingleton(slotLocker);
                    services.AddSingleton(realtime);
                    services.AddSingleton(jobs);
                    services.AddSingleton(emailRenderer);
                    services.AddSingleton(notifications);
                    services.AddSingleton(prefs);
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
                    app.UseEndpoints(endpoints => endpoints.MapAppointmentEndpoints());
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
