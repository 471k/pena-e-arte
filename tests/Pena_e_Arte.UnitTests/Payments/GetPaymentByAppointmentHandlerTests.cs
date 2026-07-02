using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Payments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class GetPaymentByAppointmentHandlerTests
{
    private readonly FakeDbContext _db          = FakeDbContext.Create();
    private readonly ICurrentUser  _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid          _studioId    = Guid.NewGuid();

    public GetPaymentByAppointmentHandlerTests() => _currentUser.Role.Returns("owner");

    private GetPaymentByAppointmentHandler CreateSut() => new(_db, _currentUser);

    [Fact]
    public async Task Handle_ExistingPayment_ReturnsPaymentResponse()
    {
        Guid appointmentId = Guid.NewGuid();
        await SeedPayment(200m, appointmentId);

        PaymentResponse? result = await CreateSut()
            .Handle(new GetPaymentByAppointmentQuery(appointmentId), default);

        result.Should().NotBeNull();
        result!.AppointmentId.Should().Be(appointmentId);
        result.Amount.Should().Be(200m);
    }

    [Fact]
    public async Task Handle_NoPaymentForAppointment_ReturnsNull()
    {
        PaymentResponse? result = await CreateSut()
            .Handle(new GetPaymentByAppointmentQuery(Guid.NewGuid()), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PaymentWithSplits_ReturnsSplitsInResponse()
    {
        Guid appointmentId = Guid.NewGuid();
        Guid paymentId     = await SeedPayment(300m, appointmentId);

        _db.SessionSplits.Add(new SessionSplit
        {
            StudioId  = _studioId,
            PaymentId = paymentId,
            Label     = "Deposit",
            Amount    = 300m
        });
        await _db.SaveChangesAsync();

        PaymentResponse? result = await CreateSut()
            .Handle(new GetPaymentByAppointmentQuery(appointmentId), default);

        result!.AppointmentId.Should().Be(appointmentId);
        result.Amount.Should().Be(300m);
        result.Splits.Should().ContainSingle(s => s.Label == "Deposit" && s.Amount == 300m);
    }

    [Fact]
    public async Task Handle_PaymentWithNoSplits_ReturnsEmptySplitsList()
    {
        Guid appointmentId = Guid.NewGuid();
        await SeedPayment(300m, appointmentId);

        PaymentResponse? result = await CreateSut()
            .Handle(new GetPaymentByAppointmentQuery(appointmentId), default);

        result!.Splits.Should().BeEmpty();
    }

    private async Task<Guid> SeedPayment(decimal amount, Guid? appointmentId = null)
    {
        Client client = new()
        {
            StudioId  = _studioId,
            FirstName = "Test",
            LastName  = "Client",
            Email     = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        Payment payment = new()
        {
            StudioId      = _studioId,
            AppointmentId = appointmentId ?? Guid.NewGuid(),
            ClientId      = client.Id,
            Amount        = amount,
            Status        = PaymentStatus.Pending
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment.Id;
    }
}
