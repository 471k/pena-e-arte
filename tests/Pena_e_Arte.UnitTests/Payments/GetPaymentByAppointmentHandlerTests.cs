using FluentAssertions;
using Pena_e_Arte.Application.Payments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class GetPaymentByAppointmentHandlerTests
{
    private readonly FakeDbContext _db       = FakeDbContext.Create();
    private readonly Guid          _studioId = Guid.NewGuid();

    private GetPaymentByAppointmentHandler CreateSut() => new(_db);

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
    public async Task Handle_PaymentWithSplits_IncludesSplits()
    {
        Guid appointmentId = Guid.NewGuid();
        Guid paymentId     = await SeedPayment(300m, appointmentId);

        _db.SessionSplits.Add(new SessionSplit
        {
            StudioId  = _studioId,
            PaymentId = paymentId,
            Label     = "Deposit",
            Amount    = 100m
        });
        _db.SessionSplits.Add(new SessionSplit
        {
            StudioId  = _studioId,
            PaymentId = paymentId,
            Label     = "Final",
            Amount    = 200m
        });
        await _db.SaveChangesAsync();

        PaymentResponse? result = await CreateSut()
            .Handle(new GetPaymentByAppointmentQuery(appointmentId), default);

        result!.SessionSplits.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_PaymentWithSoftDeletedSplits_ExcludesDeletedSplits()
    {
        Guid appointmentId = Guid.NewGuid();
        Guid paymentId     = await SeedPayment(100m, appointmentId);

        _db.SessionSplits.Add(new SessionSplit
        {
            StudioId  = _studioId,
            PaymentId = paymentId,
            Label     = "Active",
            Amount    = 100m
        });
        _db.SessionSplits.Add(new SessionSplit
        {
            StudioId  = _studioId,
            PaymentId = paymentId,
            Label     = "Deleted",
            Amount    = 50m,
            DeletedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        PaymentResponse? result = await CreateSut()
            .Handle(new GetPaymentByAppointmentQuery(appointmentId), default);

        result!.SessionSplits.Should().ContainSingle(s => s.Label == "Active");
    }

    private async Task<Guid> SeedPayment(decimal amount, Guid? appointmentId = null)
    {
        Payment payment = new()
        {
            StudioId      = _studioId,
            AppointmentId = appointmentId ?? Guid.NewGuid(),
            ClientId      = Guid.NewGuid(),
            Amount        = amount,
            Status        = PaymentStatus.Pending
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment.Id;
    }
}
