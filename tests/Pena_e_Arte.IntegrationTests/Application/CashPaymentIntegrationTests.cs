using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Application.Payments.Commands;
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
public class CashPaymentIntegrationTests
{
    private readonly DatabaseFixture _fixture;
    private readonly ISender         _sender = Substitute.For<ISender>();

    public CashPaymentIntegrationTests(DatabaseFixture fixture) => _fixture = fixture;

    // ── DeclareCashDeposit ────────────────────────────────────────────────

    [Fact]
    public async Task DeclareCashDeposit_ValidAppointment_CreatesCashPendingPayment()
    {
        Guid tenantId = Guid.NewGuid();
        (_, Guid clientId) = await SeedArtistAndClient(tenantId);
        Guid appointmentId = await SeedAppointment(tenantId, clientId, depositAmount: 80m);

        PaymentResponse result = await RunDeclareHandler(tenantId, appointmentId);

        result.Status.Should().Be(PaymentStatus.CashPending.ToString());
        result.Method.Should().Be(ClientPaymentMethod.Cash.ToString());
        result.Amount.Should().Be(80m);
        result.StripePaymentIntentId.Should().BeNull();

        await using AppDbContext verify = _fixture.CreateDbContext(tenantId);
        bool exists = await verify.Payments.AnyAsync(p => p.Id == result.Id);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task DeclareCashDeposit_DuplicateDeclaration_ThrowsDomainException()
    {
        Guid tenantId = Guid.NewGuid();
        (_, Guid clientId) = await SeedArtistAndClient(tenantId);
        Guid appointmentId = await SeedAppointment(tenantId, clientId);

        await RunDeclareHandler(tenantId, appointmentId);

        Func<Task> act = () => RunDeclareHandler(tenantId, appointmentId);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    // ── ConfirmCashDeposit ────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmCashDeposit_CashPendingPayment_SetsStatusPaidAndUpdatesDeposit()
    {
        Guid tenantId = Guid.NewGuid();
        (_, Guid clientId) = await SeedArtistAndClient(tenantId);
        Guid appointmentId = await SeedAppointment(tenantId, clientId);
        Guid paymentId     = await SeedCashPendingPayment(tenantId, appointmentId, clientId);

        Guid confirmerId  = Guid.NewGuid();
        PaymentResponse result = await RunConfirmHandler(tenantId, paymentId, confirmerId);

        result.Status.Should().Be(PaymentStatus.Paid.ToString());
        result.PaidAt.Should().NotBeNull();

        await using AppDbContext verify = _fixture.CreateDbContext(tenantId);
        Payment? p = await verify.Payments.FirstOrDefaultAsync(x => x.Id == paymentId);
        p!.CashConfirmedByUserId.Should().Be(confirmerId);

        Appointment? appt = await verify.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId);
        appt!.DepositStatus.Should().Be(DepositStatus.Paid);
    }

    [Fact]
    public async Task ConfirmCashDeposit_NotCashPayment_ThrowsDomainException()
    {
        Guid tenantId = Guid.NewGuid();
        string intentId = $"pi_{Guid.NewGuid():N}";
        (_, Guid clientId) = await SeedArtistAndClient(tenantId);
        Guid appointmentId = await SeedAppointment(tenantId, clientId);
        Guid paymentId     = await SeedCardPendingPayment(tenantId, appointmentId, clientId, intentId);

        Func<Task> act = () => RunConfirmHandler(tenantId, paymentId, Guid.NewGuid());

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*not a cash payment*");
    }

    [Fact]
    public async Task ConfirmCashDeposit_AlreadyConfirmed_ThrowsDomainException()
    {
        Guid tenantId = Guid.NewGuid();
        (_, Guid clientId) = await SeedArtistAndClient(tenantId);
        Guid appointmentId = await SeedAppointment(tenantId, clientId);
        Guid paymentId     = await SeedCashPendingPayment(
            tenantId, appointmentId, clientId, status: PaymentStatus.Paid);

        Func<Task> act = () => RunConfirmHandler(tenantId, paymentId, Guid.NewGuid());

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*already been confirmed*");
    }

    [Fact]
    public async Task ConfirmCashDeposit_ArtistOwningAppointment_Succeeds()
    {
        Guid tenantId = Guid.NewGuid();
        await using AppDbContext ctx = _fixture.CreateDbContext(tenantId);
        Guid artistUserId = Guid.NewGuid();
        Artist artist = new() { StudioId = tenantId, UserId = artistUserId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid()}@a.com" };
        Client client = new() { StudioId = tenantId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid()}@c.com" };
        ctx.Artists.Add(artist);
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();

        Appointment appt = new()
        {
            StudioId        = tenantId,
            ArtistId        = artist.Id,
            ClientId        = client.Id,
            Date            = DateTime.UtcNow.AddDays(5),
            EndDate         = DateTime.UtcNow.AddDays(5).AddMinutes(90),
            DurationMinutes = 90,
            DepositAmount   = 50m,
            Status          = AppointmentStatus.Pending,
            DepositStatus   = DepositStatus.Pending,
        };
        ctx.Appointments.Add(appt);
        await ctx.SaveChangesAsync();

        Guid paymentId = await SeedCashPendingPayment(tenantId, appt.Id, client.Id);

        await using AppDbContext db = _fixture.CreateDbContext(tenantId);
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(artistUserId);
        currentUser.Role.Returns("artist");
        ConfirmCashDepositHandler handler = new(db, currentUser, _sender);

        PaymentResponse result = await handler.Handle(new ConfirmCashDepositCommand(paymentId), default);

        result.Status.Should().Be(PaymentStatus.Paid.ToString());
    }

    [Fact]
    public async Task ConfirmCashDeposit_ArtistNotOwningAppointment_ThrowsForbidden()
    {
        Guid tenantId = Guid.NewGuid();
        (_, Guid clientId) = await SeedArtistAndClient(tenantId);
        Guid appointmentId = await SeedAppointment(tenantId, clientId);
        Guid paymentId     = await SeedCashPendingPayment(tenantId, appointmentId, clientId);

        await using AppDbContext db = _fixture.CreateDbContext(tenantId);
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.Role.Returns("artist");
        ConfirmCashDepositHandler handler = new(db, currentUser, _sender);

        Func<Task> act = () => handler.Handle(new ConfirmCashDepositCommand(paymentId), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task DeclareCashDeposit_ExistingFailedPayment_ConvertsToCash()
    {
        Guid tenantId = Guid.NewGuid();
        (_, Guid clientId) = await SeedArtistAndClient(tenantId);
        Guid appointmentId = await SeedAppointment(tenantId, clientId);
        Guid paymentId     = await SeedCashPendingPayment(
            tenantId, appointmentId, clientId, status: PaymentStatus.Failed);

        PaymentResponse result = await RunDeclareHandler(tenantId, appointmentId);

        result.Id.Should().Be(paymentId); // reused, not duplicated (unique index)
        result.Status.Should().Be(PaymentStatus.CashPending.ToString());
    }

    [Fact]
    public async Task DeclareCashDeposit_ExistingUnauthorizedCardPayment_ConvertsToCash()
    {
        Guid tenantId = Guid.NewGuid();
        (_, Guid clientId) = await SeedArtistAndClient(tenantId);
        Guid appointmentId = await SeedAppointment(tenantId, clientId);
        Guid paymentId     = await SeedCardPendingPayment(tenantId, appointmentId, clientId, $"pi_{Guid.NewGuid():N}");

        PaymentResponse result = await RunDeclareHandler(tenantId, appointmentId);

        result.Id.Should().Be(paymentId);
        result.Method.Should().Be(ClientPaymentMethod.Cash.ToString());
        result.Status.Should().Be(PaymentStatus.CashPending.ToString());
        result.StripePaymentIntentId.Should().BeNull();
    }

    // ── ActivateSubscriptionManually ──────────────────────────────────────

    [Fact]
    public async Task ActivateSubscriptionManually_NoExistingSubscription_CreatesActiveSubscription()
    {
        Guid tenantId = Guid.NewGuid();
        Guid planId   = await SeedPlan();
        await SeedStudio(tenantId);

        SubscriptionResponse result = await RunActivateHandler(tenantId, planId);

        result.Status.Should().Be(SubscriptionStatus.Active.ToString());
        result.PlanId.Should().Be(planId);
    }

    [Fact]
    public async Task ActivateSubscriptionManually_GracePeriodSubscription_SetsToActive()
    {
        Guid tenantId = Guid.NewGuid();
        Guid planId   = await SeedPlan();
        await SeedStudio(tenantId);
        await SeedSubscription(tenantId, planId, SubscriptionStatus.GracePeriod);

        SubscriptionResponse result = await RunActivateHandler(tenantId, planId);

        result.Status.Should().Be(SubscriptionStatus.Active.ToString());
    }

    [Fact]
    public async Task ActivateSubscriptionManually_GracePeriodSubscription_ClearsTrialExpiresAt()
    {
        Guid tenantId = Guid.NewGuid();
        Guid planId   = await SeedPlan();
        await SeedStudio(tenantId);
        await SeedSubscription(tenantId, planId, SubscriptionStatus.GracePeriod);

        SubscriptionResponse result = await RunActivateHandler(tenantId, planId);

        result.TrialExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task ActivateSubscriptionManually_NoExistingSubscription_LeavesTrialExpiresAtNull()
    {
        Guid tenantId = Guid.NewGuid();
        Guid planId   = await SeedPlan();
        await SeedStudio(tenantId);

        SubscriptionResponse result = await RunActivateHandler(tenantId, planId);

        result.TrialExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task ActivateSubscriptionManually_StudioNotFound_ThrowsNotFoundException()
    {
        Guid planId = await SeedPlan();

        Func<Task> act = () => RunActivateHandler(Guid.NewGuid(), planId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── Seed helpers ──────────────────────────────────────────────────────

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

    private async Task<Guid> SeedAppointment(
        Guid tenantId, Guid clientId, decimal depositAmount = 50m)
    {
        await using AppDbContext ctx = _fixture.CreateDbContext(tenantId);
        Artist artist = new() { StudioId = tenantId, FirstName = "X", LastName = "Y", Email = $"{Guid.NewGuid()}@x.com" };
        ctx.Artists.Add(artist);
        await ctx.SaveChangesAsync();

        Appointment appt = new()
        {
            StudioId        = tenantId,
            ArtistId        = artist.Id,
            ClientId        = clientId,
            Date            = DateTime.UtcNow.AddDays(5),
            EndDate         = DateTime.UtcNow.AddDays(5).AddMinutes(90),
            DurationMinutes = 90,
            DepositAmount   = depositAmount,
            Status          = AppointmentStatus.Pending,
            DepositStatus   = DepositStatus.Pending,
        };
        ctx.Appointments.Add(appt);
        await ctx.SaveChangesAsync();
        return appt.Id;
    }

    private async Task<Guid> SeedCashPendingPayment(
        Guid tenantId, Guid appointmentId, Guid clientId,
        PaymentStatus status = PaymentStatus.CashPending)
    {
        await using AppDbContext ctx = _fixture.CreateDbContext(tenantId);
        Payment payment = new()
        {
            StudioId      = tenantId,
            AppointmentId = appointmentId,
            ClientId      = clientId,
            Amount        = 50m,
            Method        = ClientPaymentMethod.Cash,
            Status        = status,
        };
        ctx.Payments.Add(payment);
        await ctx.SaveChangesAsync();
        return payment.Id;
    }

    private async Task<Guid> SeedCardPendingPayment(
        Guid tenantId, Guid appointmentId, Guid clientId, string intentId)
    {
        await using AppDbContext ctx = _fixture.CreateDbContext(tenantId);
        Payment payment = new()
        {
            StudioId              = tenantId,
            AppointmentId         = appointmentId,
            ClientId              = clientId,
            Amount                = 50m,
            Method                = ClientPaymentMethod.Card,
            Status                = PaymentStatus.Pending,
            StripePaymentIntentId = intentId,
        };
        ctx.Payments.Add(payment);
        await ctx.SaveChangesAsync();
        return payment.Id;
    }

    private async Task SeedStudio(Guid tenantId)
    {
        await using AppDbContext ctx = _fixture.CreateDbContext(Guid.Empty);
        if (await ctx.Studios.AnyAsync(s => s.Id == tenantId)) return;
        ctx.Studios.Add(new Studio
        {
            Id   = tenantId,
            Name = "Cash Test Studio",
            Slug = tenantId.ToString("N")[..8],
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<Guid> SeedPlan()
    {
        await using AppDbContext ctx = _fixture.CreateDbContext(Guid.Empty);
        Plan plan = new()
        {
            Name             = $"Test Plan {Guid.NewGuid():N}",
            BillingInterval  = BillingInterval.Monthly,
            PriceMonthly     = 29m,
            PriceYearly      = 299m,
        };
        ctx.Plans.Add(plan);
        await ctx.SaveChangesAsync();
        return plan.Id;
    }

    private async Task SeedSubscription(
        Guid tenantId, Guid planId, SubscriptionStatus status)
    {
        await using AppDbContext ctx = _fixture.CreateDbContext(Guid.Empty);
        Subscription sub = new()
        {
            StudioId         = tenantId,
            PlanId           = planId,
            Status           = status,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(-1),
            TrialExpiresAt   = DateTime.UtcNow.AddDays(-10),
        };
        ctx.Subscriptions.Add(sub);
        await ctx.SaveChangesAsync();
    }

    // ── Runner helpers ────────────────────────────────────────────────────

    private async Task<PaymentResponse> RunDeclareHandler(Guid tenantId, Guid appointmentId)
    {
        await using AppDbContext db = _fixture.CreateDbContext(tenantId);
        CurrentTenantService tenant = new();
        tenant.SetTenant(tenantId);
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.Role.Returns("owner"); // staff path — no ownership restriction
        IStripePaymentService stripe = Substitute.For<IStripePaymentService>();
        DeclareCashDepositHandler handler = new(db, tenant, currentUser, stripe);
        return await handler.Handle(new DeclareCashDepositCommand(appointmentId, null), default);
    }

    private async Task<PaymentResponse> RunConfirmHandler(
        Guid tenantId, Guid paymentId, Guid confirmerUserId)
    {
        await using AppDbContext db  = _fixture.CreateDbContext(tenantId);
        ICurrentUser currentUser     = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(confirmerUserId);
        ConfirmCashDepositHandler handler = new(db, currentUser, _sender);
        return await handler.Handle(new ConfirmCashDepositCommand(paymentId), default);
    }

    private async Task<SubscriptionResponse> RunActivateHandler(Guid studioId, Guid planId)
    {
        await using AppDbContext db = _fixture.CreateDbContext(Guid.Empty);
        ActivateSubscriptionManuallyHandler handler = new(db, NullLogger<ActivateSubscriptionManuallyHandler>.Instance);
        return await handler.Handle(
            new ActivateSubscriptionManuallyCommand(studioId, planId, "Cash payment received"), default);
    }
}
