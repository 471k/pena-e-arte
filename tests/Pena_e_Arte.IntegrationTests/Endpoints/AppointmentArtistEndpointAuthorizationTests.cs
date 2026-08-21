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

// Exercises PATCH /api/v1/appointments/{id}/artist and POST /api/v1/appointments through the
// REAL ASP.NET Core authorization pipeline (AddApiAuthorization()'s "OwnerOnly"/"ClientAndAbove"
// policies + app.UseAuthorization()) end-to-end — mirrors ClientArtistEndpointAuthorizationTests'
// precedent for the same reason: a metadata-only or handler-only test cannot see a future
// regression where a policy is silently weakened.
[Collection("Database")]
public class AppointmentArtistEndpointAuthorizationTests(DatabaseFixture fixture)
{
    private const string SigningKeyValue = "appointment-artist-endpoint-test-key-32-bytes!";

    [Fact]
    public async Task PatchAppointmentArtist_OwnerToken_Returns200()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid appointmentId, Guid artistId, _) = await SeedUnassignedAppointmentAndArtistAsync(tenantId);

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(tenantId, "owner");

        HttpResponseMessage response = await SendAssign(client, token, appointmentId, artistId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AppointmentResponse? body = await response.Content.ReadFromJsonAsync<AppointmentResponse>();
        body!.ArtistId.Should().Be(artistId);
    }

    [Fact]
    public async Task PatchAppointmentArtist_ArtistToken_Returns403()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid appointmentId, Guid artistId, _) = await SeedUnassignedAppointmentAndArtistAsync(tenantId);

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(tenantId, "artist");

        HttpResponseMessage response = await SendAssign(client, token, appointmentId, artistId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PatchAppointmentArtist_NoToken_Returns401()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid appointmentId, Guid artistId, _) = await SeedUnassignedAppointmentAndArtistAsync(tenantId);

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();

        HttpRequestMessage request = new(HttpMethod.Patch, $"/api/v1/appointments/{appointmentId}/artist")
        {
            Content = JsonContent.Create(new AssignAppointmentArtistRequest(artistId)),
        };
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostAppointment_NoArtistId_Returns201WithNullArtistId()
    {
        Guid tenantId = Guid.NewGuid();
        // Also seeds an active artist with a full-day schedule every day — the "any artist
        // available" check this studio-choice booking relies on.
        (_, _, Guid clientId) = await SeedUnassignedAppointmentAndArtistAsync(tenantId);

        using IHost host = await BuildHost();
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();
        string token = BuildToken(tenantId, "owner");

        CreateAppointmentRequest body = new(
            null, clientId, DateTime.UtcNow.AddDays(3), 90, null);
        HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/appointments")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Authorization", $"Bearer {token}");
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        AppointmentResponse? result = await response.Content.ReadFromJsonAsync<AppointmentResponse>();
        result!.ArtistId.Should().BeNull();
    }

    private async Task<(Guid appointmentId, Guid artistId, Guid clientId)> SeedUnassignedAppointmentAndArtistAsync(Guid tenantId)
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

        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            db.ArtistSchedules.Add(new ArtistSchedule
            {
                StudioId = tenantId,
                ArtistId = artist.Id,
                DayOfWeek = day,
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
                IsAvailable = true,
            });
        }

        Appointment appointment = new()
        {
            StudioId = tenantId,
            ArtistId = null,
            ClientId = client.Id,
            Date = DateTime.UtcNow.AddDays(3),
            EndDate = DateTime.UtcNow.AddDays(3).AddMinutes(90),
            DurationMinutes = 90,
            Status = AppointmentStatus.Pending,
            DepositStatus = DepositStatus.Pending,
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        return (appointment.Id, artist.Id, client.Id);
    }

    private static async Task<HttpResponseMessage> SendAssign(
        HttpClient client, string token, Guid appointmentId, Guid artistId)
    {
        HttpRequestMessage request = new(HttpMethod.Patch, $"/api/v1/appointments/{appointmentId}/artist")
        {
            Content = JsonContent.Create(new AssignAppointmentArtistRequest(artistId)),
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
