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
    public async Task Handle_AuthorizedPayment_CallsStripeCapture()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(PaymentStatus.Captured, "pi_test");

        await CreateSut().Handle(new CaptureDepositCommand(paymentId), default);

        await _stripe.Received(1)
            .CapturePaymentAsync("pi_test", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AuthorizedPayment_UpdatesPaymentStatusToPaid()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(PaymentStatus.Captured, "pi_test");

        await CreateSut().Handle(new CaptureDepositCommand(paymentId), default);

        _db.Payments.Single(p => p.Id == paymentId).Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public async Task Handle_AuthorizedPayment_SetsPaidAt()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(PaymentStatus.Captured, "pi_test");

        await CreateSut().Handle(new CaptureDepositCommand(paymentId), default);

        _db.Payments.Single(p => p.Id == paymentId).PaidAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_AuthorizedPayment_UpdatesAppointmentDepositStatusToPaid()
    {
        await SeedStudio();
        await SeedAppointment();
        Guid paymentId = await SeedPayment(PaymentStatus.Captured, "pi_test");

        await CreateSut().Handle(new CaptureDepositCommand(paymentId), default);

        _db.Appointments.Single(a => a.Id == _appointmentId).DepositStatus
            .Should().Be(DepositStatus.Paid);
    }

    [Fact]
    public async Task Handle_AuthorizedPayment_NotifiesRealtime()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(PaymentStatus.Captured, "pi_test");

        await CreateSut().Handle(new CaptureDepositCommand(paymentId), default);

        await _realtime.Received(1)
            .NotifyStudioAsync(_studioId, "DepositCaptured", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AuthorizedPayment_ReturnsPaymentResponse()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(PaymentStatus.Captured, "pi_test");

        PaymentResponse result = await CreateSut().Handle(new CaptureDepositCommand(paymentId), default);

        result.Id.Should().Be(paymentId);
        result.Status.Should().Be(PaymentStatus.Paid.ToString());
    }

    [Fact]
    public async Task Handle_PendingNotYetAuthorized_ThrowsBusinessRuleViolationException()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(PaymentStatus.Pending, "pi_test");

        Func<Task> act = () => CreateSut().Handle(new CaptureDepositCommand(paymentId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*not completed card authorization*");
        await _stripe.DidNotReceive().CapturePaymentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
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
            .WithMessage("*authorized*");
    }

    [Fact]
    public async Task Handle_NoStripeIntentId_ThrowsBusinessRuleViolationException()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(PaymentStatus.Captured, stripeIntentId: null);

        Func<Task> act = () => CreateSut().Handle(new CaptureDepositCommand(paymentId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*Stripe intent*");
    }

    private async Task SeedStudio()
    {
        _db.Studios.Add(new Studio
        {
            Id   = _studioId,
            Name = "Test Studio",
            Slug = "test"
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
            StudioId              = _studioId,
            AppointmentId         = _appointmentId,
            ClientId              = client.Id,
            Amount                = 100m,
            Status                = status,
            StripePaymentIntentId = stripeIntentId
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment.Id;
    }
}
