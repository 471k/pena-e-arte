using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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

// The platform admin's internal note on a feedback report (AdminNote; Help: "only visible to you,
// not the sender") must never reach the submitter. Checked on the SERIALISED response through the
// real pipeline against real MySQL: the property must be absent from the JSON, not merely null,
// because "no screen shows it" is a property of today's frontend, not of the API.
// Same host setup as DepositRuleDefaultsEndpointTests.
[Collection("Database")]
public class FeedbackSubmitterEndpointTests(DatabaseFixture fixture)
{
    private const string SigningKeyValue = "feedback-submitter-endpoint-test-key-32-bytes!!";
    private const string Note = "internal: likely a reseller, do not refund";

    [Fact]
    public async Task GetMine_ReportWithAdminNote_JsonHasNoAdminNoteProperty()
    {
        Guid studioId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Guid reportId = await SeedReportWithNoteAsync(studioId, userId);

        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Get, "/api/v1/feedback/mine", BuildToken(studioId, userId, "client"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string raw = await response.Content.ReadAsStringAsync();
        using JsonDocument doc = JsonDocument.Parse(raw);
        doc.RootElement.GetArrayLength().Should().Be(1);
        JsonElement item = doc.RootElement[0];
        item.GetProperty("id").GetGuid().Should().Be(reportId);
        PropertyNames(item).Should().NotContain(n => n.Equals("adminNote", StringComparison.OrdinalIgnoreCase));
        raw.Should().NotContain("reseller");
    }

    [Fact]
    public async Task Submit_ResponseJsonHasNoAdminNoteProperty()
    {
        Guid studioId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        await SeedStudioAsync(studioId);

        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/feedback")
        {
            Content = JsonContent.Create(new SubmitFeedbackRequest(
                "SupportRequest", "Cannot upload", "The upload button spins forever on my phone.")),
        };
        request.Headers.Add("Authorization", $"Bearer {BuildToken(studioId, userId, "client")}");
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        PropertyNames(doc.RootElement).Should().Contain("title");
        PropertyNames(doc.RootElement).Should().NotContain(n => n.Equals("adminNote", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AdminInbox_StillReturnsTheAdminNote()
    {
        Guid studioId = Guid.NewGuid();
        Guid reportId = await SeedReportWithNoteAsync(studioId, Guid.NewGuid());

        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Get, "/api/v1/platform/feedback", BuildToken(Guid.NewGuid(), Guid.NewGuid(), "admin"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement ours = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == reportId);
        ours.GetProperty("adminNote").GetString().Should().Be(Note);
    }

    private static List<string> PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).ToList();

    private async Task SeedStudioAsync(Guid studioId)
    {
        await using AppDbContext db = fixture.CreateDbContext(studioId);
        db.Studios.Add(new Studio
        {
            Id = studioId, Name = "Feedback", Slug = studioId.ToString("N")[..8], CountryCode = "PT", Currency = "EUR",
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedReportWithNoteAsync(Guid studioId, Guid userId)
    {
        await using AppDbContext db = fixture.CreateDbContext(studioId);
        FeedbackReport report = FeedbackReport.Create(
            studioId, userId, "client", "Feedback", FeedbackType.SupportRequest,
            "Refund please", "I was charged twice for the same booking.");
        report.UpdateStatus(FeedbackStatus.Reviewing, Note);
        db.FeedbackReports.Add(report);
        await db.SaveChangesAsync();
        return report.Id;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, string token)
    {
        HttpRequestMessage request = new(method, url);
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await client.SendAsync(request);
    }

    private async Task<IHost> BuildHost()
    {
        IIdentityService identity = Substitute.For<IIdentityService>();
        IRealtimeNotifier realtime = Substitute.For<IRealtimeNotifier>();
        IJobScheduler jobs = Substitute.For<IJobScheduler>();
        // SubmitFeedbackValidator takes IR2Service (it checks attachment keys); none are sent here.
        IR2Service r2 = Substitute.For<IR2Service>();
        // TenantMiddleware's method-injected parameter is resolved on every request.
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
                    services.AddSingleton(identity);
                    services.AddSingleton(realtime);
                    services.AddSingleton(jobs);
                    services.AddSingleton(r2);
                    services.AddSingleton(subscriptions);

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseMySql(fixture.ConnectionString, new MySqlServerVersion(new Version(8, 0, 0))));
                    services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

                    Assembly applicationAssembly = typeof(ValidationBehavior<,>).Assembly;
                    services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(applicationAssembly));
                    services.AddValidatorsFromAssembly(applicationAssembly);
                    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

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
                    app.UseEndpoints(endpoints => endpoints.MapFeedbackEndpoints());
                });
            });

        return await builder.StartAsync();
    }

    private static string BuildToken(Guid tenantId, Guid userId, string role) =>
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
