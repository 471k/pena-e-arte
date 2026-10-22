using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Payments;
using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class CreateDepositPaymentHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IPaymentProvider _provider = Substitute.For<IPaymentProvider>();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _clientUserId = Guid.NewGuid();

    public CreateDepositPaymentHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _currentUser.UserId.Returns(_clientUserId);
        _currentUser.Role.Returns("client");
        _provider.Capabilities.Returns(new PaymentProviderCapabilities(
            SupportsAuthCapture: true, SupportsHoldExpiry: true, SupportedCurrencies: ["ALL", "EUR"]));
        _provider.CreatePaymentHoldAsync(Arg.Any<PaymentHoldRequest>(), Arg.Any<CancellationToken>())
               .Returns(("pi_new", "secret_new"));

        _db.Studios.Add(new Studio { Id = _studioId, Name = "Test", Slug = "test", CountryCode = "AL", Currency = "ALL" });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private CreateDepositPaymentHandler CreateSut() => new(_db, _tenant, _currentUser, _provider, _jobs);

    [Fact]
    public async Task Handle_OwnAppointment_CreatesPendingCardPayment()
    {
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId, depositAmount: 80m);

        PaymentIntentResponse result = await CreateSut()
            .Handle(new CreateDepositPaymentCommand(appointmentId), default);

        result.ClientToken.Should().Be("secret_new");
        Payment stored = _db.Payments.Single(p => p.AppointmentId == appointmentId);
        stored.Method.Should().Be(ClientPaymentMethod.Card);
        stored.Status.Should().Be(PaymentStatus.Pending);
        stored.Amount.Should().Be(80m);
        stored.Currency.Should().Be("ALL");
        await _provider.Received(1).CreatePaymentHoldAsync(
            Arg.Is<PaymentHoldRequest>(r => r.Amount == 80m && r.Currency == "ALL"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EuroStudio_CreatesCardHoldInEuro()
    {
        _db.Studios.Single(s => s.Id == _studioId).Currency = "EUR";
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId, depositAmount: 50m);

        await CreateSut().Handle(new CreateDepositPaymentCommand(appointmentId), default);

        Payment stored = _db.Payments.Single(p => p.AppointmentId == appointmentId);
        stored.Currency.Should().Be("EUR");
        await _provider.Received(1).CreatePaymentHoldAsync(
            Arg.Is<PaymentHoldRequest>(r => r.Amount == 50m && r.Currency == "EUR"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_JpyStudio_ThrowsBusinessRuleViolationAndNeverCallsProvider()
    {
        _db.Studios.Single(s => s.Id == _studioId).Currency = "JPY";
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId, depositAmount: 3000m);

        Func<Task> act = () => CreateSut().Handle(new CreateDepositPaymentCommand(appointmentId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>().WithMessage("*JPY*");
        await _provider.DidNotReceive().CreatePaymentHoldAsync(
            Arg.Any<PaymentHoldRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingPendingCardPayment_StillAwaitingClient_ResumesExistingSecret()
    {
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId);
        await SeedPayment(appointmentId, ClientPaymentMethod.Card, PaymentStatus.Pending,
            intentId: "pi_old", clientToken: "secret_old");
        _provider.GetStatusAsync(Arg.Any<Guid>(), "pi_old", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Pending);

        PaymentIntentResponse result = await CreateSut()
            .Handle(new CreateDepositPaymentCommand(appointmentId), default);

        result.ClientToken.Should().Be("secret_old");
        await _provider.DidNotReceive().CreatePaymentHoldAsync(
            Arg.Any<PaymentHoldRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LegacyLinkWithoutExpiry_OlderThan24h_CancelsAndMintsFreshHold()
    {
        // No HoldExpiresAt (a payment link from before links carried one): POK's 24 h default ran
        // from UpdatedAt, so a row last minted 25 h ago is an expired order — never resume it.
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId);
        await SeedPayment(appointmentId, ClientPaymentMethod.Card, PaymentStatus.Pending,
            intentId: "pi_legacy", clientToken: "secret_legacy", updatedAt: DateTime.UtcNow.AddHours(-25));
        _provider.GetStatusAsync(Arg.Any<Guid>(), "pi_legacy", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Pending);

        PaymentIntentResponse result = await CreateSut()
            .Handle(new CreateDepositPaymentCommand(appointmentId), default);

        await _provider.Received(1).CancelAsync(_studioId, "pi_legacy", Arg.Any<CancellationToken>());
        await _provider.Received(1).CreatePaymentHoldAsync(Arg.Any<PaymentHoldRequest>(), Arg.Any<CancellationToken>());
        result.ClientToken.Should().Be("secret_new");
    }

    [Fact]
    public async Task Handle_PendingHoldNearExpiry_CancelsAndMintsFreshHold()
    {
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId);
        Guid paymentId = await SeedPayment(appointmentId, ClientPaymentMethod.Card, PaymentStatus.Pending,
            intentId: "pi_expiring", clientToken: "secret_expiring", holdExpiresAt: DateTime.UtcNow.AddMinutes(5));
        _provider.GetStatusAsync(Arg.Any<Guid>(), "pi_expiring", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Pending);

        PaymentIntentResponse result = await CreateSut()
            .Handle(new CreateDepositPaymentCommand(appointmentId), default);

        await _provider.Received(1).CancelAsync(_studioId, "pi_expiring", Arg.Any<CancellationToken>());
        await _provider.Received(1).CreatePaymentHoldAsync(Arg.Any<PaymentHoldRequest>(), Arg.Any<CancellationToken>());
        result.PaymentId.Should().Be(paymentId); // same row, fresh order
        _db.Payments.Single(p => p.Id == paymentId).ProviderReferenceId.Should().Be("pi_new");
    }

    [Fact]
    public async Task Handle_PendingHoldWellInsideWindow_Resumes()
    {
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId);
        await SeedPayment(appointmentId, ClientPaymentMethod.Card, PaymentStatus.Pending,
            intentId: "pi_fresh", clientToken: "secret_fresh", holdExpiresAt: DateTime.UtcNow.AddHours(12));
        _provider.GetStatusAsync(Arg.Any<Guid>(), "pi_fresh", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Pending);

        PaymentIntentResponse result = await CreateSut()
            .Handle(new CreateDepositPaymentCommand(appointmentId), default);

        result.ClientToken.Should().Be("secret_fresh");
        await _provider.DidNotReceive().CancelAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _provider.DidNotReceive().CreatePaymentHoldAsync(Arg.Any<PaymentHoldRequest>(), Arg.Any<CancellationToken>());
    }

    private void SwitchCardPaymentsOff() =>
        _provider.Capabilities.Returns(new PaymentProviderCapabilities(
            SupportsAuthCapture: true, SupportsHoldExpiry: true, SupportedCurrencies: ["ALL", "EUR"],
            CardPaymentsEnabled: false));

    [Fact]
    public async Task Handle_CardPaymentsSwitchedOff_RefusesANewHoldWithoutCallingTheProvider()
    {
        SwitchCardPaymentsOff();
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId, depositAmount: 80m);

        Func<Task> act = () => CreateSut().Handle(new CreateDepositPaymentCommand(appointmentId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage(CardCurrencyGuard.CardPaymentsSwitchedOffMessage);
        await _provider.DidNotReceive().CreatePaymentHoldAsync(Arg.Any<PaymentHoldRequest>(), Arg.Any<CancellationToken>());
        _db.Payments.Any(p => p.AppointmentId == appointmentId).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_CardPaymentsSwitchedOff_DoesNotResumeAnUnpaidOrderCreatedBefore()
    {
        SwitchCardPaymentsOff();
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId);
        await SeedPayment(appointmentId, ClientPaymentMethod.Card, PaymentStatus.Pending,
            intentId: "pi_open", clientToken: "secret_open", holdExpiresAt: DateTime.UtcNow.AddHours(12));
        _provider.GetStatusAsync(Arg.Any<Guid>(), "pi_open", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Pending);

        Func<Task> act = () => CreateSut().Handle(new CreateDepositPaymentCommand(appointmentId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage(CardCurrencyGuard.CardPaymentsSwitchedOffMessage);
        await _provider.DidNotReceive().CreatePaymentHoldAsync(Arg.Any<PaymentHoldRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CardPaymentsSwitchedOff_StillHealsAPaymentAlreadyAuthorised()
    {
        // Money already taken must never be stranded by the switch.
        SwitchCardPaymentsOff();
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId);
        Guid paymentId = await SeedPayment(appointmentId, ClientPaymentMethod.Card, PaymentStatus.Pending,
            intentId: "pi_paid_elsewhere", clientToken: "secret_paid", holdExpiresAt: DateTime.UtcNow.AddHours(12));
        _provider.GetStatusAsync(Arg.Any<Guid>(), "pi_paid_elsewhere", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Authorized);

        await CreateSut().Handle(new CreateDepositPaymentCommand(appointmentId), default);

        _db.Payments.Single(p => p.Id == paymentId).Status.Should().Be(PaymentStatus.Captured);
    }

    [Fact]
    public async Task Handle_ExistingCardIntentCancelledAtProvider_MintsFreshIntent()
    {
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId);
        Guid paymentId = await SeedPayment(appointmentId, ClientPaymentMethod.Card, PaymentStatus.Pending,
            intentId: "pi_dead", clientToken: "secret_dead");
        _provider.GetStatusAsync(Arg.Any<Guid>(), "pi_dead", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Canceled);

        PaymentIntentResponse result = await CreateSut()
            .Handle(new CreateDepositPaymentCommand(appointmentId), default);

        result.PaymentId.Should().Be(paymentId); // same row, fresh intent
        result.ClientToken.Should().Be("secret_new");
        _db.Payments.Single(p => p.Id == paymentId).ProviderReferenceId.Should().Be("pi_new");
    }

    [Fact]
    public async Task Handle_IntentAuthorizedButWebhookMissed_HealsToCaptured()
    {
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId);
        Guid paymentId = await SeedPayment(appointmentId, ClientPaymentMethod.Card, PaymentStatus.Pending,
            intentId: "pi_held", clientToken: "secret_held");
        _provider.GetStatusAsync(Arg.Any<Guid>(), "pi_held", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Authorized);

        PaymentIntentResponse result = await CreateSut()
            .Handle(new CreateDepositPaymentCommand(appointmentId), default);

        result.Status.Should().Be(PaymentStatus.Captured.ToString());
        _db.Payments.Single(p => p.Id == paymentId).Status.Should().Be(PaymentStatus.Captured);
    }

    [Fact]
    public async Task Handle_IntentSucceededButWebhookMissed_HealsToPaidAndUpdatesDeposit()
    {
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId);
        Guid paymentId = await SeedPayment(appointmentId, ClientPaymentMethod.Card, PaymentStatus.Pending,
            intentId: "pi_done", clientToken: "secret_done");
        _provider.GetStatusAsync(Arg.Any<Guid>(), "pi_done", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Captured);

        PaymentIntentResponse result = await CreateSut()
            .Handle(new CreateDepositPaymentCommand(appointmentId), default);

        result.Status.Should().Be(PaymentStatus.Paid.ToString());
        _db.Payments.Single(p => p.Id == paymentId).Status.Should().Be(PaymentStatus.Paid);
        _db.Appointments.Single(a => a.Id == appointmentId).DepositStatus.Should().Be(DepositStatus.Paid);
        _jobs.Received(1).EnqueueDepositPaidNotification(paymentId);
    }

    [Fact]
    public async Task Handle_NewDeposit_IsChargedWhenTheClientPays_NoHold()
    {
        // C-4 (2026-10-22): charge at booking.
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId);

        await CreateSut().Handle(new CreateDepositPaymentCommand(appointmentId), default);

        await _provider.Received(1).CreatePaymentHoldAsync(
            Arg.Is<PaymentHoldRequest>(r => r.CaptureImmediately),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CancelledAppointment_RefusesAndNeverCallsProvider()
    {
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId);
        await CancelAppointment(appointmentId);

        Func<Task> act = () => CreateSut().Handle(new CreateDepositPaymentCommand(appointmentId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>().WithMessage("*cancelled*");
        await _provider.DidNotReceive().CreatePaymentHoldAsync(Arg.Any<PaymentHoldRequest>(), Arg.Any<CancellationToken>());
        _jobs.DidNotReceive().TriggerPaymentReconciliationNow();
    }

    [Fact]
    public async Task Handle_CancelledAppointment_WithCardPaymentInFlight_LeavesItToReconciliation()
    {
        // The client may have paid on POK's page after the cancellation: never mark it Paid here;
        // PaymentReconciliationJob refunds it in full.
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId);
        Guid paymentId = await SeedPayment(appointmentId, ClientPaymentMethod.Card, PaymentStatus.Pending,
            intentId: "pi_inflight", clientToken: "secret_inflight");
        await CancelAppointment(appointmentId);

        Func<Task> act = () => CreateSut().Handle(new CreateDepositPaymentCommand(appointmentId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
        _jobs.Received(1).TriggerPaymentReconciliationNow();
        _db.Payments.Single(p => p.Id == paymentId).Status.Should().Be(PaymentStatus.Pending);
        await _provider.DidNotReceive().GetStatusAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private async Task CancelAppointment(Guid appointmentId)
    {
        _db.Appointments.Single(a => a.Id == appointmentId).Status = AppointmentStatus.Cancelled;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Handle_ExistingCashPendingPayment_ConvertsToCard()
    {
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId);
        Guid paymentId = await SeedPayment(appointmentId, ClientPaymentMethod.Cash, PaymentStatus.CashPending,
            intentId: null, clientToken: null, cashNote: "will pay at studio");

        PaymentIntentResponse result = await CreateSut()
            .Handle(new CreateDepositPaymentCommand(appointmentId), default);

        result.PaymentId.Should().Be(paymentId);
        Payment stored = _db.Payments.Single(p => p.Id == paymentId);
        stored.Method.Should().Be(ClientPaymentMethod.Card);
        stored.Status.Should().Be(PaymentStatus.Pending);
        stored.CashNote.Should().BeNull();
        stored.ProviderReferenceId.Should().Be("pi_new");
    }

    [Fact]
    public async Task Handle_AppointmentOfAnotherClient_ThrowsNotFoundException()
    {
        Guid otherClientId = await SeedClient(Guid.NewGuid());
        Guid appointmentId = await SeedAppointment(otherClientId);

        Func<Task> act = () => CreateSut().Handle(new CreateDepositPaymentCommand(appointmentId), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_StaffRole_DoesNotRequireOwnership()
    {
        _currentUser.Role.Returns("owner");
        Guid otherClientId = await SeedClient(Guid.NewGuid());
        Guid appointmentId = await SeedAppointment(otherClientId);

        PaymentIntentResponse result = await CreateSut()
            .Handle(new CreateDepositPaymentCommand(appointmentId), default);

        result.ClientToken.Should().Be("secret_new");
    }

    [Fact]
    public async Task Handle_NoDepositRequired_ThrowsBusinessRuleViolation()
    {
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId, depositAmount: 0m);

        Func<Task> act = () => CreateSut().Handle(new CreateDepositPaymentCommand(appointmentId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*does not require a deposit*");
    }

    [Fact]
    public async Task Handle_ExistingFailedPayment_ReusesRowWithFreshIntent()
    {
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId);
        Guid paymentId = await SeedPayment(appointmentId, ClientPaymentMethod.Card, PaymentStatus.Failed,
            intentId: "pi_failed", clientToken: "secret_failed");

        PaymentIntentResponse result = await CreateSut()
            .Handle(new CreateDepositPaymentCommand(appointmentId), default);

        result.PaymentId.Should().Be(paymentId); // same row, not a duplicate
        Payment stored = _db.Payments.Single(p => p.Id == paymentId);
        stored.Status.Should().Be(PaymentStatus.Pending);
        stored.ProviderReferenceId.Should().Be("pi_new");
    }

    [Fact]
    public async Task Handle_PaymentAlreadyAuthorized_ThrowsBusinessRuleViolation()
    {
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId);
        await SeedPayment(appointmentId, ClientPaymentMethod.Card, PaymentStatus.Captured,
            intentId: "pi_held", clientToken: "secret_held");

        Func<Task> act = () => CreateSut().Handle(new CreateDepositPaymentCommand(appointmentId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*already in progress*");
    }

    private async Task<Guid> SeedClient(Guid userId)
    {
        Client client = new()
        {
            StudioId = _studioId,
            UserId = userId,
            FirstName = "Test",
            LastName = "Client",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return client.Id;
    }

    private async Task<Guid> SeedAppointment(Guid clientId, decimal depositAmount = 50m)
    {
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = Guid.NewGuid(),
            ClientId = clientId,
            Date = DateTime.UtcNow.AddDays(5),
            EndDate = DateTime.UtcNow.AddDays(5).AddMinutes(90),
            DurationMinutes = 90,
            Status = AppointmentStatus.Pending,
            DepositStatus = DepositStatus.Pending,
            DepositAmount = depositAmount,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return appointment.Id;
    }

    private async Task<Guid> SeedPayment(
        Guid appointmentId, ClientPaymentMethod method, PaymentStatus status,
        string? intentId, string? clientToken, string? cashNote = null,
        DateTime? holdExpiresAt = null, DateTime? updatedAt = null)
    {
        Payment payment = new()
        {
            StudioId = _studioId,
            AppointmentId = appointmentId,
            ClientId = _clientUserId,
            Amount = 50m,
            Method = method,
            Status = status,
            ProviderReferenceId = intentId,
            ClientToken = clientToken,
            CashNote = cashNote,
            HoldExpiresAt = holdExpiresAt,
        };
        if (updatedAt is { } seededUpdatedAt)
            payment.UpdatedAt = seededUpdatedAt;
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return payment.Id;
    }
}
