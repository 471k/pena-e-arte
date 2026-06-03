using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Application.Payments.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class PaymentHandlerIntegrationTests
{
    private readonly DatabaseFixture      _fixture;
    private readonly IStripePaymentService _stripe;
    private readonly IRealtimeNotifier    _realtime;

    public PaymentHandlerIntegrationTests(DatabaseFixture fixture)
    {
        _fixture  = fixture;
        _stripe   = Substitute.For<IStripePaymentService>();
        _realtime = Substitute.For<IRealtimeNotifier>();

        _stripe.CreatePaymentIntentAsync(
                Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(),
                Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(($"pi_{Guid.NewGuid():N}", $"pi_{Guid.NewGuid():N}_secret"));

        _stripe.RefundPaymentIntentAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns($"re_{Guid.NewGuid():N}");
    }

    // ── CreatePaymentIntent ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePaymentIntent_WithValidAppointment_PersistsPayment()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);
        Guid appointmentId = await SeedAppointment(tenantId, artistId, clientId);
        await SeedStudio(tenantId);

        PaymentIntentResponse result = await RunCreateHandler(tenantId, appointmentId, clientId);

        await using AppDbContext verify = _fixture.CreateDbContext(tenantId);
        bool exists = await verify.Payments.AnyAsync(p => p.Id == result.PaymentId);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task CreatePaymentIntent_WithValidAppointment_ReturnsClientSecret()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);
        Guid appointmentId = await SeedAppointment(tenantId, artistId, clientId);
        await SeedStudio(tenantId);

        PaymentIntentResponse result = await RunCreateHandler(tenantId, appointmentId, clientId);

        result.ClientSecret.Should().NotBeNullOrEmpty();
        result.Status.Should().Be(PaymentStatus.Pending.ToString());
    }

    [Fact]
    public async Task CreatePaymentIntent_DuplicateForSameAppointment_ThrowsBusinessRuleViolation()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);
        Guid appointmentId = await SeedAppointment(tenantId, artistId, clientId);
        await SeedStudio(tenantId);
        await RunCreateHandler(tenantId, appointmentId, clientId);

        Func<Task> act = () => RunCreateHandler(tenantId, appointmentId, clientId);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task CreatePaymentIntent_StudioHasNoStripeAccount_ThrowsStripeAccountNotConnectedException()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);
        Guid appointmentId = await SeedAppointment(tenantId, artistId, clientId);
        await SeedStudio(tenantId, stripeAccountId: null);

        Func<Task> act = () => RunCreateHandler(tenantId, appointmentId, clientId);

        await act.Should().ThrowAsync<StripeAccountNotConnectedException>();
    }

    // ── UpdateSessionSplits ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateSessionSplits_WithValidSplits_PersistsSplits()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);
        Guid appointmentId = await SeedAppointment(tenantId, artistId, clientId);
        await SeedStudio(tenantId);

        PaymentIntentResponse payment = await RunCreateHandler(tenantId, appointmentId, clientId, 300m);

        await using AppDbContext db = _fixture.CreateDbContext(tenantId);
        UpdateSessionSplitsHandler handler = new(db);
        await handler.Handle(new UpdateSessionSplitsCommand(payment.PaymentId,
            new UpdateSessionSplitsRequest([
                new SessionSplitItem("Deposit", 100m),
                new SessionSplitItem("Final",   200m)
            ])), default);

        await using AppDbContext verify = _fixture.CreateDbContext(tenantId);
        int count = await verify.SessionSplits
            .CountAsync(ss => ss.PaymentId == payment.PaymentId && ss.DeletedAt == null);
        count.Should().Be(2);
    }

    [Fact]
    public async Task UpdateSessionSplits_SumMismatch_ThrowsBusinessRuleViolation()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);
        Guid appointmentId = await SeedAppointment(tenantId, artistId, clientId);
        await SeedStudio(tenantId);

        PaymentIntentResponse payment = await RunCreateHandler(tenantId, appointmentId, clientId, 300m);

        await using AppDbContext db = _fixture.CreateDbContext(tenantId);
        UpdateSessionSplitsHandler handler = new(db);

        Func<Task> act = () => handler.Handle(new UpdateSessionSplitsCommand(payment.PaymentId,
            new UpdateSessionSplitsRequest([new SessionSplitItem("Only", 100m)])), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    // ── ConfirmPayment / MarkPaymentFailed (webhook handlers) ────────────────────

    [Fact]
    public async Task ConfirmPayment_ExistingIntent_UpdatesStatusToPaid()
    {
        Guid tenantId      = Guid.NewGuid();
        string intentId    = $"pi_{Guid.NewGuid():N}";
        Guid paymentId     = await SeedPendingPayment(tenantId, intentId);

        await using AppDbContext db = _fixture.CreateDbContext(Guid.Empty);
        ConfirmPaymentHandler handler = new(db);
        await handler.Handle(new ConfirmPaymentCommand(intentId), default);

        await using AppDbContext verify = _fixture.CreateDbContext(tenantId);
        Payment? payment = await verify.Payments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == paymentId);
        payment!.Status.Should().Be(PaymentStatus.Paid);
        payment.PaidAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ConfirmPayment_AlreadyPaid_IsIdempotent()
    {
        Guid tenantId  = Guid.NewGuid();
        string intentId = $"pi_{Guid.NewGuid():N}";
        await SeedPendingPayment(tenantId, intentId, PaymentStatus.Paid);

        // Should not throw
        await using AppDbContext db = _fixture.CreateDbContext(Guid.Empty);
        ConfirmPaymentHandler handler = new(db);
        Func<Task> act = () => handler.Handle(new ConfirmPaymentCommand(intentId), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ConfirmPayment_UnknownIntent_SilentlyIgnores()
    {
        await using AppDbContext db = _fixture.CreateDbContext(Guid.Empty);
        ConfirmPaymentHandler handler = new(db);

        Func<Task> act = () => handler.Handle(new ConfirmPaymentCommand("pi_unknown_000"), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task MarkPaymentFailed_ExistingIntent_UpdatesStatusToFailed()
    {
        Guid tenantId  = Guid.NewGuid();
        string intentId = $"pi_{Guid.NewGuid():N}";
        Guid paymentId  = await SeedPendingPayment(tenantId, intentId);

        await using AppDbContext db = _fixture.CreateDbContext(Guid.Empty);
        MarkPaymentFailedHandler handler = new(db);
        await handler.Handle(new MarkPaymentFailedCommand(intentId), default);

        await using AppDbContext verify = _fixture.CreateDbContext(tenantId);
        Payment? payment = await verify.Payments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == paymentId);
        payment!.Status.Should().Be(PaymentStatus.Failed);
    }

    // ── GetPayments / GetPaymentByAppointment ─────────────────────────────────────

    [Fact]
    public async Task GetPaymentByAppointment_ExistingPayment_ReturnsPayment()
    {
        Guid tenantId      = Guid.NewGuid();
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);
        Guid appointmentId = await SeedAppointment(tenantId, artistId, clientId);
        await SeedStudio(tenantId);
        await RunCreateHandler(tenantId, appointmentId, clientId);

        await using AppDbContext db = _fixture.CreateDbContext(tenantId);
        GetPaymentByAppointmentHandler handler = new(db);
        PaymentResponse? result = await handler.Handle(
            new GetPaymentByAppointmentQuery(appointmentId), default);

        result.Should().NotBeNull();
        result!.AppointmentId.Should().Be(appointmentId);
    }

    [Fact]
    public async Task GetPaymentByAppointment_NoPayment_ReturnsNull()
    {
        Guid tenantId = Guid.NewGuid();

        await using AppDbContext db = _fixture.CreateDbContext(tenantId);
        GetPaymentByAppointmentHandler handler = new(db);
        PaymentResponse? result = await handler.Handle(
            new GetPaymentByAppointmentQuery(Guid.NewGuid()), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPayments_MultiplePayments_ReturnsPagedResults()
    {
        Guid tenantId = Guid.NewGuid();
        await SeedStudio(tenantId);
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);

        for (int i = 0; i < 3; i++)
        {
            Guid apptId = await SeedAppointment(tenantId, artistId, clientId);
            await RunCreateHandler(tenantId, apptId, clientId);
        }

        await using AppDbContext db = _fixture.CreateDbContext(tenantId);
        GetPaymentsHandler handler = new(db);
        List<PaymentResponse> result = await handler.Handle(new GetPaymentsQuery(PageSize: 2), default);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPayments_DifferentTenant_ReturnsOnlyOwnPayments()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();

        await SeedStudio(tenantA);
        await SeedStudio(tenantB);

        (Guid artistA, Guid clientA) = await SeedArtistAndClient(tenantA);
        (Guid artistB, Guid clientB) = await SeedArtistAndClient(tenantB);

        Guid apptA = await SeedAppointment(tenantA, artistA, clientA);
        Guid apptB = await SeedAppointment(tenantB, artistB, clientB);

        await RunCreateHandler(tenantA, apptA, clientA);
        await RunCreateHandler(tenantB, apptB, clientB);

        await using AppDbContext db = _fixture.CreateDbContext(tenantA);
        GetPaymentsHandler handler = new(db);
        List<PaymentResponse> result = await handler.Handle(new GetPaymentsQuery(), default);

        result.Should().ContainSingle(p => p.StudioId == tenantA);
    }

    // ── Seed helpers ──────────────────────────────────────────────────────────────

    private async Task<(Guid ArtistId, Guid ClientId)> SeedArtistAndClient(Guid tenantId)
    {
        await using AppDbContext ctx = _fixture.CreateDbContext(tenantId);
        Artist artist = new() { StudioId = tenantId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid()}@a.com" };
        Client client = new() { StudioId = tenantId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid()}@c.com" };
        ctx.Artists.Add(artist);
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();
        return (artist.Id, client.Id);
    }

    private async Task<Guid> SeedAppointment(Guid tenantId, Guid artistId, Guid clientId)
    {
        await using AppDbContext ctx = _fixture.CreateDbContext(tenantId);
        Appointment appt = new()
        {
            StudioId        = tenantId,
            ArtistId        = artistId,
            ClientId        = clientId,
            Date            = DateTime.UtcNow.AddDays(3),
            EndDate         = DateTime.UtcNow.AddDays(3).AddMinutes(90),
            DurationMinutes = 90,
            Status          = AppointmentStatus.Pending,
            DepositStatus   = DepositStatus.Pending
        };
        ctx.Appointments.Add(appt);
        await ctx.SaveChangesAsync();
        return appt.Id;
    }

    private async Task SeedStudio(Guid tenantId, string? stripeAccountId = "acct_test_123")
    {
        await using AppDbContext ctx = _fixture.CreateDbContext(Guid.Empty);
        if (await ctx.Studios.AnyAsync(s => s.Id == tenantId)) return;
        ctx.Studios.Add(new Studio
        {
            Id              = tenantId,
            Name            = "Test Studio",
            Slug            = tenantId.ToString("N")[..8],
            StripeAccountId = stripeAccountId
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<Guid> SeedPendingPayment(
        Guid tenantId, string intentId, PaymentStatus status = PaymentStatus.Pending)
    {
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);
        Guid appointmentId = await SeedAppointment(tenantId, artistId, clientId);

        await using AppDbContext ctx = _fixture.CreateDbContext(tenantId);
        Payment payment = new()
        {
            StudioId              = tenantId,
            AppointmentId         = appointmentId,
            ClientId              = clientId,
            Amount                = 100m,
            Status                = status,
            StripePaymentIntentId = intentId
        };
        ctx.Payments.Add(payment);
        await ctx.SaveChangesAsync();
        return payment.Id;
    }

    private async Task<PaymentIntentResponse> RunCreateHandler(
        Guid tenantId, Guid appointmentId, Guid clientId, decimal amount = 200m)
    {
        await using AppDbContext db = _fixture.CreateDbContext(tenantId);
        CurrentTenantService tenant = new();
        tenant.SetTenant(tenantId);
        CreatePaymentIntentHandler handler = new(db, tenant, _stripe, _realtime);
        return await handler.Handle(
            new CreatePaymentIntentCommand(new CreatePaymentIntentRequest(appointmentId, clientId, amount, "eur")),
            default);
    }
}
