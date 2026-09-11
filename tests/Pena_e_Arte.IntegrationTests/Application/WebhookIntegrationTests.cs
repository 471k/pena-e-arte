using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Webhooks.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

// Same rationale as ExternalApiIntegrationTests: the single most important property of a
// per-studio outbound-webhook secret/endpoint is that it can never leak across tenants, and
// FakeDbContext (used by the unit tests) applies no query filter at all — only provable
// against real MySQL and the real AppDbContext.
[Collection("Database")]
public class WebhookIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task WebhookEndpoints_ScopedToTenant_StudioACannotSeeStudioBsEndpoint()
    {
        Guid studioAId = Guid.NewGuid();
        Guid studioBId = Guid.NewGuid();
        await SeedStudioAsync(studioAId, allowApiAccess: true);
        await SeedStudioAsync(studioBId, allowApiAccess: true);

        await using AppDbContext dbA = fixture.CreateDbContext(studioAId);
        await new UpsertWebhookEndpointHandler(dbA, new StubCurrentTenant(studioAId), new StubTokenEncryptor())
            .Handle(new UpsertWebhookEndpointCommand("https://a.example.com/hook"), default);

        await using AppDbContext dbB = fixture.CreateDbContext(studioBId);
        await new UpsertWebhookEndpointHandler(dbB, new StubCurrentTenant(studioBId), new StubTokenEncryptor())
            .Handle(new UpsertWebhookEndpointCommand("https://b.example.com/hook"), default);

        await using AppDbContext readAsA = fixture.CreateDbContext(studioAId);
        WebhookEndpoint onlyVisible = await readAsA.WebhookEndpoints.SingleAsync();
        onlyVisible.Url.Should().Be("https://a.example.com/hook");
    }

    [Fact]
    public async Task UpsertWebhookEndpoint_StudioOnFreePlan_ThrowsBusinessRuleViolationException()
    {
        Guid studioId = Guid.NewGuid();
        await SeedStudioAsync(studioId, allowApiAccess: false);

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        UpsertWebhookEndpointHandler handler = new(db, new StubCurrentTenant(studioId), new StubTokenEncryptor());

        Func<Task> act = () => handler.Handle(new UpsertWebhookEndpointCommand("https://example.com/hook"), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task WebhookDeliveryJob_SuccessfulDelivery_SignsRequestAndRecordsSuccess()
    {
        Guid studioId = Guid.NewGuid();
        string rawSecret = await SeedActiveEndpointAsync(studioId, "https://example.com/hook");
        Guid appointmentId = await SeedAppointmentAsync(studioId);

        CapturingHandler handler = new(HttpStatusCode.OK);
        await using AppDbContext jobDb = CreateJob(handler, out WebhookDeliveryJob job);

        await job.DeliverAsync(studioId, "appointment.created", appointmentId, default);

        handler.LastRequest.Should().NotBeNull();
        string timestamp = handler.LastRequest!.Headers.GetValues("X-Webhook-Timestamp").Single();
        string signature = handler.LastRequest.Headers.GetValues("X-Webhook-Signature").Single();
        string expectedSignature = "sha256=" + WebhookSigner.Sign(rawSecret, timestamp, handler.LastRequestBody!);
        signature.Should().Be(expectedSignature);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        WebhookEndpoint endpoint = await verify.WebhookEndpoints.SingleAsync();
        endpoint.LastDeliverySucceeded.Should().BeTrue();
        endpoint.ConsecutiveFailureCount.Should().Be(0);

        WebhookDelivery delivery = await verify.WebhookDeliveries.SingleAsync();
        delivery.Succeeded.Should().BeTrue();
        delivery.ResponseStatusCode.Should().Be(200);
        delivery.EventType.Should().Be("appointment.created");
    }

    [Fact]
    public async Task WebhookDeliveryJob_NonSuccessResponse_RecordsFailureAndThrowsForHangfireRetry()
    {
        Guid studioId = Guid.NewGuid();
        await SeedActiveEndpointAsync(studioId, "https://example.com/hook");
        Guid appointmentId = await SeedAppointmentAsync(studioId);

        await using AppDbContext jobDb = CreateJob(new CapturingHandler(HttpStatusCode.InternalServerError), out WebhookDeliveryJob job);

        Func<Task> act = () => job.DeliverAsync(studioId, "appointment.created", appointmentId, default);

        await act.Should().ThrowAsync<InvalidOperationException>();

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        WebhookDelivery delivery = await verify.WebhookDeliveries.SingleAsync();
        delivery.Succeeded.Should().BeFalse();
        delivery.ResponseStatusCode.Should().Be(500);
    }

    [Fact]
    public async Task WebhookDeliveryJob_TwentiethConsecutiveFailure_AutoDisablesEndpoint()
    {
        Guid studioId = Guid.NewGuid();
        await SeedActiveEndpointAsync(studioId, "https://example.com/hook");
        Guid appointmentId = await SeedAppointmentAsync(studioId);

        for (int i = 0; i < 20; i++)
        {
            await using AppDbContext jobDb = CreateJob(new CapturingHandler(HttpStatusCode.InternalServerError), out WebhookDeliveryJob job);
            try { await job.DeliverAsync(studioId, "appointment.created", appointmentId, default); }
            catch (InvalidOperationException) { /* expected each time — see job's own retry-signal design */ }
        }

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        WebhookEndpoint endpoint = await verify.WebhookEndpoints.SingleAsync();
        endpoint.IsActive.Should().BeFalse();
        endpoint.ConsecutiveFailureCount.Should().Be(20);
    }

    [Fact]
    public async Task WebhookDeliveryJob_NoActiveEndpoint_DoesNothingAndRecordsNoDelivery()
    {
        Guid studioId = Guid.NewGuid();
        await SeedStudioAsync(studioId, allowApiAccess: true);
        Guid appointmentId = await SeedAppointmentAsync(studioId);

        CapturingHandler handler = new(HttpStatusCode.OK);
        await using AppDbContext jobDb = CreateJob(handler, out WebhookDeliveryJob job);

        await job.DeliverAsync(studioId, "appointment.created", appointmentId, default);

        handler.LastRequest.Should().BeNull();
        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        (await verify.WebhookDeliveries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task WebhookDeliveryJob_MissingResource_SkipsWithoutRecordingOrThrowing()
    {
        Guid studioId = Guid.NewGuid();
        await SeedActiveEndpointAsync(studioId, "https://example.com/hook");

        CapturingHandler handler = new(HttpStatusCode.OK);
        await using AppDbContext jobDb = CreateJob(handler, out WebhookDeliveryJob job);

        await job.DeliverAsync(studioId, "appointment.created", Guid.NewGuid(), default);

        handler.LastRequest.Should().BeNull();
        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        (await verify.WebhookDeliveries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task WebhookDeliveryJob_PingEvent_DeliversStaticPayload()
    {
        Guid studioId = Guid.NewGuid();
        await SeedActiveEndpointAsync(studioId, "https://example.com/hook");

        CapturingHandler handler = new(HttpStatusCode.OK);
        await using AppDbContext jobDb = CreateJob(handler, out WebhookDeliveryJob job);

        await job.DeliverAsync(studioId, "ping", Guid.Empty, default);

        handler.LastRequestBody.Should().Contain("\"type\":\"ping\"");
    }

    // Hangfire jobs run with no ambient tenant (see WebhookDeliveryJob's own doc comment) —
    // Guid.Empty here mirrors every other "no tenant" test context in this suite
    // (SeedStudioAsync/SeedAppointmentAsync use the same convention). The returned db is the
    // caller's to dispose.
    private AppDbContext CreateJob(HttpMessageHandler handler, out WebhookDeliveryJob job)
    {
        AppDbContext db = fixture.CreateDbContext(Guid.Empty);

        IHttpClientFactory httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient("Webhooks").Returns(_ => new HttpClient(handler));

        job = new WebhookDeliveryJob(db, httpFactory, new StubTokenEncryptor(), NullLogger<WebhookDeliveryJob>.Instance);
        return db;
    }

    private async Task<string> SeedActiveEndpointAsync(Guid studioId, string url)
    {
        await SeedStudioAsync(studioId, allowApiAccess: true);
        string rawSecret = WebhookSigner.GenerateSecret();

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        db.WebhookEndpoints.Add(new WebhookEndpoint
        {
            StudioId = studioId,
            Url = url,
            EncryptedSecret = new StubTokenEncryptor().Encrypt(rawSecret),
            IsActive = true,
        });
        await db.SaveChangesAsync();
        return rawSecret;
    }

    private async Task SeedStudioAsync(Guid studioId, bool allowApiAccess)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        Plan plan = new() { Name = $"Plan-{Guid.NewGuid():N}", AllowApiAccess = allowApiAccess };
        db.Plans.Add(plan);
        db.Studios.Add(new Studio { Id = studioId, Name = "Test Studio", Slug = $"studio-{Guid.NewGuid():N}", City = "Lisbon" });
        db.Subscriptions.Add(new Subscription { StudioId = studioId, PlanId = plan.Id });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedAppointmentAsync(Guid studioId)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        Client client = new() { StudioId = studioId, FirstName = "Ana", LastName = "Silva", Email = $"{Guid.NewGuid():N}@test.com" };
        Artist artist = new() { StudioId = studioId, FirstName = "Elena", LastName = "Martins", Email = $"{Guid.NewGuid():N}@test.com" };
        db.Clients.Add(client);
        db.Artists.Add(artist);
        await db.SaveChangesAsync();

        Appointment appointment = new()
        {
            StudioId = studioId,
            ArtistId = artist.Id,
            ClientId = client.Id,
            Date = DateTime.UtcNow.AddDays(1),
            EndDate = DateTime.UtcNow.AddDays(1).AddHours(1),
            DurationMinutes = 60,
            Status = AppointmentStatus.Pending,
            DepositStatus = DepositStatus.Pending,
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.Id;
    }

    private sealed class StubCurrentTenant(Guid studioId) : ICurrentTenant
    {
        public Guid StudioId { get; private set; } = studioId;
        public bool IsSet => true;
        public void SetTenant(Guid studioId) => StudioId = studioId;
    }

    private sealed class StubTokenEncryptor : ITokenEncryptor
    {
        public string Encrypt(string plainText) => $"encrypted:{plainText}";
        public string Decrypt(string cipherText) => cipherText.Replace("encrypted:", "");
    }

    private sealed class CapturingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        }
    }
}
