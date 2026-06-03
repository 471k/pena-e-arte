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

public class CaptureDepositHandlerTests
{
    private readonly FakeDbContext        _db        = FakeDbContext.Create();
    private readonly ICurrentTenant       _tenant    = Substitute.For<ICurrentTenant>();
    private readonly IStripePaymentService _stripe    = Substitute.For<IStripePaymentService>();
    private readonly IRealtimeNotifier    _realtime  = Substitute.For<IRealtimeNotifier>();
    private readonly Guid                 _studioId  = Guid.NewGuid();
    private readonly Guid                 _appointmentId = Guid.NewGuid();

    public CaptureDepositHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
    }

    private CaptureDepositHandler CreateSut() => new(_db, _tenant, _stripe, _realtime);

    [Fact]
    public async Task Handle_PendingPayment_CallsStripeCapture()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(PaymentStatus.Pending, "pi_test");

        await CreateSut().Handle(new CaptureDepositCommand(paymentId), default);

        await _stripe.Received(1)
            .CapturePaymentAsync("pi_test", "acct_test", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PendingPayment_UpdatesPaymentStatusToPaid()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(PaymentStatus.Pending, "pi_test");

        await CreateSut().Handle(new CaptureDepositCommand(paymentId), default);

        _db.Payments.Single(p => p.Id == paymentId).Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public async Task Handle_PendingPayment_SetsPaidAt()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(PaymentStatus.Pending, "pi_test");

        await CreateSut().Handle(new CaptureDepositCommand(paymentId), default);

        _db.Payments.Single(p => p.Id == paymentId).PaidAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_PendingPayment_UpdatesAppointmentDepositStatusToPaid()
    {
        await SeedStudio();
        await SeedAppointment();
        Guid paymentId = await SeedPayment(PaymentStatus.Pending, "pi_test");

        await CreateSut().Handle(new CaptureDepositCommand(paymentId), default);

        _db.Appointments.Single(a => a.Id == _appointmentId).DepositStatus
            .Should().Be(DepositStatus.Paid);
    }

    [Fact]
    public async Task Handle_PendingPayment_NotifiesRealtime()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(PaymentStatus.Pending, "pi_test");

        await CreateSut().Handle(new CaptureDepositCommand(paymentId), default);

        await _realtime.Received(1)
            .NotifyStudioAsync(_studioId, "DepositCaptured", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PendingPayment_ReturnsPaymentResponse()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(PaymentStatus.Pending, "pi_test");

        PaymentResponse result = await CreateSut().Handle(new CaptureDepositCommand(paymentId), default);

        result.Id.Should().Be(paymentId);
        result.Status.Should().Be(PaymentStatus.Paid.ToString());
    }

    [Fact]
    public async Task Handle_PaymentNotFound_ThrowsNotFoundException()
    {
        await SeedStudio();

        Func<Task> act = () => CreateSut().Handle(new CaptureDepositCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_AlreadyPaidPayment_ThrowsBusinessRuleViolationException()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(PaymentStatus.Paid, "pi_test");

        Func<Task> act = () => CreateSut().Handle(new CaptureDepositCommand(paymentId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*pending*");
    }

    [Fact]
    public async Task Handle_NoStripeIntentId_ThrowsBusinessRuleViolationException()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(PaymentStatus.Pending, stripeIntentId: null);

        Func<Task> act = () => CreateSut().Handle(new CaptureDepositCommand(paymentId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*Stripe intent*");
    }

    [Fact]
    public async Task Handle_StudioHasNoStripeAccount_ThrowsStripeAccountNotConnectedException()
    {
        await SeedStudio(stripeAccountId: null);
        Guid paymentId = await SeedPayment(PaymentStatus.Pending, "pi_test");

        Func<Task> act = () => CreateSut().Handle(new CaptureDepositCommand(paymentId), default);

        await act.Should().ThrowAsync<StripeAccountNotConnectedException>();
    }

    private async Task SeedStudio(string? stripeAccountId = "acct_test")
    {
        _db.Studios.Add(new Studio
        {
            Id              = _studioId,
            Name            = "Test Studio",
            Slug            = "test",
            StripeAccountId = stripeAccountId
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedAppointment()
    {
        _db.Appointments.Add(new Appointment
        {
            Id            = _appointmentId,
            StudioId      = _studioId,
            ArtistId      = Guid.NewGuid(),
            ClientId      = Guid.NewGuid(),
            Date          = DateTime.UtcNow.AddDays(3),
            EndDate       = DateTime.UtcNow.AddDays(3).AddMinutes(90),
            DurationMinutes = 90,
            Status        = AppointmentStatus.Pending,
            DepositStatus = DepositStatus.Pending
        });
        await _db.SaveChangesAsync();
    }

    private async Task<Guid> SeedPayment(PaymentStatus status, string? stripeIntentId)
    {
        Payment payment = new()
        {
            StudioId              = _studioId,
            AppointmentId         = _appointmentId,
            ClientId              = Guid.NewGuid(),
            Amount                = 100m,
            Status                = status,
            StripePaymentIntentId = stripeIntentId
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment.Id;
    }
}
