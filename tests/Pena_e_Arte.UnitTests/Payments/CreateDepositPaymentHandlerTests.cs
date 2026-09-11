using FluentAssertions;
using NSubstitute;
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
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _clientUserId = Guid.NewGuid();

    public CreateDepositPaymentHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _currentUser.UserId.Returns(_clientUserId);
        _currentUser.Role.Returns("client");
        _provider.CreatePaymentHoldAsync(Arg.Any<PaymentHoldRequest>(), Arg.Any<CancellationToken>())
               .Returns(("pi_new", "secret_new"));
    }

    private CreateDepositPaymentHandler CreateSut() => new(_db, _tenant, _currentUser, _provider);

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
        await _provider.Received(1).CreatePaymentHoldAsync(
            Arg.Is<PaymentHoldRequest>(r => r.AmountInCents == 8000 && r.Currency == "ALL"),
            Arg.Any<CancellationToken>());
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
        string? intentId, string? clientToken, string? cashNote = null)
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
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return payment.Id;
    }
}
