using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

/// <summary>
/// Covers the command the standalone /pay/:paymentId checkout page calls right after the POK
/// widget's own onSuccess fires — that callback is UX only, never a source of truth (ADR-0001).
/// </summary>
public class ConfirmCardPaymentHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IPaymentProvider _provider = Substitute.For<IPaymentProvider>();
    private readonly Guid _studioId = Guid.NewGuid();

    public ConfirmCardPaymentHandlerTests() => _currentUser.Role.Returns("owner"); // no ownership check by default

    private ConfirmCardPaymentHandler CreateSut() => new(_db, _currentUser, _provider);

    [Fact]
    public async Task Handle_ProviderReportsAuthorized_HealsPendingToCaptured()
    {
        Guid paymentId = await SeedPaymentAsync(PaymentStatus.Pending, "order-1");
        _provider.GetStatusAsync(_studioId, "order-1", Arg.Any<CancellationToken>())
            .Returns(PaymentProviderStatus.Authorized);

        PaymentResponse result = await CreateSut().Handle(new ConfirmCardPaymentCommand(paymentId), default);

        result.Status.Should().Be(PaymentStatus.Captured.ToString());
        _db.Payments.Single(p => p.Id == paymentId).Status.Should().Be(PaymentStatus.Captured);
    }

    [Fact]
    public async Task Handle_ProviderReportsCaptured_HealsToPaidAndUpdatesAppointmentDeposit()
    {
        Guid appointmentId = await SeedAppointmentAsync();
        Guid paymentId = await SeedPaymentAsync(PaymentStatus.Pending, "order-2", appointmentId);
        _provider.GetStatusAsync(_studioId, "order-2", Arg.Any<CancellationToken>())
            .Returns(PaymentProviderStatus.Captured);

        PaymentResponse result = await CreateSut().Handle(new ConfirmCardPaymentCommand(paymentId), default);

        result.Status.Should().Be(PaymentStatus.Paid.ToString());
        _db.Payments.Single(p => p.Id == paymentId).PaidAt.Should().NotBeNull();
        _db.Appointments.Single(a => a.Id == appointmentId).DepositStatus.Should().Be(DepositStatus.Paid);
    }

    [Fact]
    public async Task Handle_ProviderStillPending_LeavesLocalStateUnchanged()
    {
        Guid paymentId = await SeedPaymentAsync(PaymentStatus.Pending, "order-3");
        _provider.GetStatusAsync(_studioId, "order-3", Arg.Any<CancellationToken>())
            .Returns(PaymentProviderStatus.Pending);

        PaymentResponse result = await CreateSut().Handle(new ConfirmCardPaymentCommand(paymentId), default);

        result.Status.Should().Be(PaymentStatus.Pending.ToString());
        _db.Payments.Single(p => p.Id == paymentId).Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public async Task Handle_AlreadyPaid_DoesNotCallProviderAgain()
    {
        Guid paymentId = await SeedPaymentAsync(PaymentStatus.Paid, "order-4");

        await CreateSut().Handle(new ConfirmCardPaymentCommand(paymentId), default);

        await _provider.DidNotReceive().GetStatusAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CashPayment_DoesNotCallProvider()
    {
        Guid payment = await SeedCashPaymentAsync();

        await CreateSut().Handle(new ConfirmCardPaymentCommand(payment), default);

        await _provider.DidNotReceive().GetStatusAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ClientRole_DifferentClient_ThrowsUnauthorized()
    {
        Guid paymentId = await SeedPaymentAsync(PaymentStatus.Pending, "order-5");
        _currentUser.Role.Returns("client");
        _currentUser.UserId.Returns(Guid.NewGuid()); // not linked to any client

        Func<Task> act = () => CreateSut().Handle(new ConfirmCardPaymentCommand(paymentId), default);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    private async Task<Guid> SeedPaymentAsync(PaymentStatus status, string providerReferenceId, Guid? appointmentId = null)
    {
        Guid clientId = await SeedClientAsync();
        Payment payment = new()
        {
            StudioId = _studioId,
            AppointmentId = appointmentId ?? Guid.NewGuid(),
            ClientId = clientId,
            Amount = 50m,
            Status = status,
            Method = ClientPaymentMethod.Card,
            ProviderReferenceId = providerReferenceId,
            ClientToken = providerReferenceId,
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment.Id;
    }

    private async Task<Guid> SeedCashPaymentAsync()
    {
        Guid clientId = await SeedClientAsync();
        Payment payment = new()
        {
            StudioId = _studioId,
            AppointmentId = Guid.NewGuid(),
            ClientId = clientId,
            Amount = 50m,
            Status = PaymentStatus.CashPending,
            Method = ClientPaymentMethod.Cash,
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment.Id;
    }

    private async Task<Guid> SeedClientAsync()
    {
        Client client = new()
        {
            StudioId = _studioId,
            FirstName = "Test",
            LastName = "Client",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        return client.Id;
    }

    private async Task<Guid> SeedAppointmentAsync()
    {
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Date = DateTime.UtcNow.AddDays(5),
            EndDate = DateTime.UtcNow.AddDays(5).AddMinutes(90),
            DurationMinutes = 90,
            Status = AppointmentStatus.Pending,
            DepositStatus = DepositStatus.Pending,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        return appointment.Id;
    }
}
