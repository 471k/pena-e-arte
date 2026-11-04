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

// E04a: GET /api/v1/appointments/{id}/preparation and GET /api/v1/appointments?assignedToMe=true through
// the real authorization pipeline ("ArtistAndAbove"), validator scanning and real MySQL: the assigned
// artist gets the staff-worded preparation, another artist gets 404, a client is refused, and the staff
// list's summary agrees with the preparation's items for the same session.
[Collection("Database")]
public class AppointmentPreparationEndpointTests(DatabaseFixture fixture)
{
    private const string SigningKeyValue = "appointment-preparation-endpoint-test-key!";

    [Fact]
    public async Task NoToken_Returns401()
    {
        Seeded s = await SeedAsync();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await client.GetAsync($"/api/v1/appointments/{s.AppointmentId}/preparation");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AClient_IsRefused()
    {
        Seeded s = await SeedAsync();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await Get(client, BuildToken(s.TenantId, "client", s.ClientUserId), $"/api/v1/appointments/{s.AppointmentId}/preparation");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TheAssignedArtist_GetsTheStaffWordedPreparation_ThatAgreesWithTheirListSummary()
    {
        Seeded s = await SeedAsync();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();
        string token = BuildToken(s.TenantId, "artist", s.ArtistUserId);

        HttpResponseMessage response = await Get(client, token, $"/api/v1/appointments/{s.AppointmentId}/preparation");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AppointmentPreparationResponse? prep = await response.Content.ReadFromJsonAsync<AppointmentPreparationResponse>();
        prep!.Items.Select(i => (i.Key, i.Satisfied, i.State)).Should().Equal(
            ("DepositPaid", true, "Secured"), ("ConsentForm", false, "Not signed yet"));
        prep.Deposit!.Status.Should().Be("Paid");

        List<AppointmentResponse>? list = await (await Get(client, token, "/api/v1/appointments?assignedToMe=true"))
            .Content.ReadFromJsonAsync<List<AppointmentResponse>>();
        AppointmentResponse row = list!.Single();
        row.Readiness.Should().Be(new AppointmentReadinessSummaryResponse(prep.Items.Count, prep.Items.Count(i => i.Satisfied)));
        row.SafetyNotes.Should().BeNull();
    }

    [Fact]
    public async Task AnotherArtist_Gets404_Not403()
    {
        Seeded s = await SeedAsync();
        Guid otherArtistUserId = Guid.NewGuid();
        await using (AppDbContext ctx = fixture.CreateDbContext(s.TenantId))
        {
            ctx.Artists.Add(new Artist { StudioId = s.TenantId, UserId = otherArtistUserId, FirstName = "O", LastName = "T", Email = $"{Guid.NewGuid():N}@o.test", IsActive = true });
            await ctx.SaveChangesAsync();
        }
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await Get(client, BuildToken(s.TenantId, "artist", otherArtistUserId), $"/api/v1/appointments/{s.AppointmentId}/preparation");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ToBeforeFrom_IsRejectedByTheValidator()
    {
        Seeded s = await SeedAsync();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await Get(client, BuildToken(s.TenantId, "artist", s.ArtistUserId),
            "/api/v1/appointments?from=2026-11-05T00:00:00Z&to=2026-11-04T00:00:00Z");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private static async Task<HttpResponseMessage> Get(HttpClient client, string token, string url)
    {
        HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await client.SendAsync(request);
    }

    private sealed record Seeded(Guid TenantId, Guid AppointmentId, Guid ArtistUserId, Guid ClientUserId);

    private async Task<Seeded> SeedAsync()
    {
        Guid tenantId = Guid.NewGuid();
        Guid artistUserId = Guid.NewGuid();
        Guid clientUserId = Guid.NewGuid();
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        ctx.Studios.Add(new Studio { Id = tenantId, Name = "Prep", Slug = tenantId.ToString("N")[..8], CountryCode = "PT", Currency = "EUR" });
        Artist artist = new() { StudioId = tenantId, UserId = artistUserId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid():N}@a.test", IsActive = true };
        Client client = new() { StudioId = tenantId, UserId = clientUserId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid():N}@c.test" };
        Service service = new()
        {
            StudioId = tenantId,
            Name = "Session",
            DurationMinutes = 60,
            IsActive = true,
            RequiresDepositPaid = true,
            RequiresConsentForm = true,
        };
        ctx.Artists.Add(artist);
        ctx.Clients.Add(client);
        ctx.Services.Add(service);
        await ctx.SaveChangesAsync();
        DateTime start = DateTime.UtcNow.AddDays(7);
        Appointment appointment = new()
        {
            StudioId = tenantId,
            ArtistId = artist.Id,
            ClientId = client.Id,
            ServiceId = service.Id,
            Date = start,
            EndDate = start.AddHours(1),
            DurationMinutes = 60,
            Status = AppointmentStatus.Confirmed,
            DepositStatus = DepositStatus.Paid,
            DepositAmount = 50m,
        };
        ctx.Appointments.Add(appointment);
        ctx.BookingIntakes.Add(new BookingIntake { StudioId = tenantId, AppointmentId = appointment.Id, TattooDescription = "A rose", SafetyNotes = "medical detail" });
        ctx.Payments.Add(new Payment { StudioId = tenantId, AppointmentId = appointment.Id, ClientId = client.Id, Amount = 50m, Currency = "EUR", Status = PaymentStatus.Paid, PaidAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        return new Seeded(tenantId, appointment.Id, artistUserId, clientUserId);
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
                    services.AddSingleton(Substitute.For<IIdentityService>());

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
