using FluentAssertions;
using MediatR;
using NSubstitute;
using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class RefundPaymentHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IPaymentProvider _stripe = Substitute.For<IPaymentProvider>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly Guid _studioId = Guid.NewGuid();

    public RefundPaymentHandlerTests()
    {
        _stripe.RefundAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("re_test_123");
    }

    private RefundPaymentHandler CreateSut() => new(_db, _stripe, _realtime, _sender);

    [Fact]
    public async Task Handle_PaidPayment_ReturnsRefundedStatus()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(200m, PaymentStatus.Paid, "pi_test");

        PaymentResponse result = await CreateSut()
            .Handle(new RefundPaymentCommand(paymentId, null), default);

        result.Status.Should().Be(PaymentStatus.Refunded.ToString());
    }

    [Fact]
    public async Task Handle_PaidPayment_PersistsRefundedStatus()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(200m, PaymentStatus.Paid, "pi_test");

        await CreateSut().Handle(new RefundPaymentCommand(paymentId, null), default);

        _db.Payments.Single(p => p.Id == paymentId).Status.Should().Be(PaymentStatus.Refunded);
    }

    [Fact]
    public async Task Handle_PaidPayment_CallsStripeRefund()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(200m, PaymentStatus.Paid, "pi_test");

        await CreateSut().Handle(new RefundPaymentCommand(paymentId, null), default);

        await _stripe.Received(1).RefundAsync(
            _studioId, "pi_test", 200m, "EUR", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PartialRefund_PassesCorrectAmountToStripe()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(200m, PaymentStatus.Paid, "pi_test");

        await CreateSut().Handle(new RefundPaymentCommand(paymentId, 50m), default);

        await _stripe.Received(1).RefundAsync(
            _studioId, "pi_test", 50m, "EUR", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PartialRefund_RecordsRefundedAmount()
    {
        // Regression: revenue reporting relies on RefundedAmount to distinguish a partial
        // refund from a full one (Status alone is Refunded either way).
        await SeedStudio();
        Guid paymentId = await SeedPayment(200m, PaymentStatus.Paid, "pi_test");

        await CreateSut().Handle(new RefundPaymentCommand(paymentId, 50m), default);

        _db.Payments.Single(p => p.Id == paymentId).RefundedAmount.Should().Be(50m);
    }

    [Fact]
    public async Task Handle_FullRefund_RecordsRefundedAmountAsFullOriginalAmount()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(200m, PaymentStatus.Paid, "pi_test");

        await CreateSut().Handle(new RefundPaymentCommand(paymentId, null), default);

        _db.Payments.Single(p => p.Id == paymentId).RefundedAmount.Should().Be(200m);
    }

    [Fact]
    public async Task Handle_UnpaidPayment_ThrowsBusinessRuleViolationException()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(200m, PaymentStatus.Pending, null);

        Func<Task> act = () => CreateSut().Handle(new RefundPaymentCommand(paymentId, null), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*paid*");
    }

    [Fact]
    public async Task Handle_AmountExceedsOriginal_ThrowsBusinessRuleViolationException()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(200m, PaymentStatus.Paid, "pi_test");

        Func<Task> act = () => CreateSut().Handle(new RefundPaymentCommand(paymentId, 300m), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*exceed*");
    }

    [Fact]
    public async Task Handle_RefundAmountHasMoreDecimalsThanCurrencyAllows_ThrowsBusinessRuleViolationException()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(200m, PaymentStatus.Paid, "pi_test");

        Func<Task> act = () => CreateSut().Handle(new RefundPaymentCommand(paymentId, 12.345m), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*EUR*");
    }

    [Fact]
    public async Task Handle_PaymentNotFound_ThrowsNotFoundException()
    {
        await SeedStudio();

        Func<Task> act = () => CreateSut().Handle(new RefundPaymentCommand(Guid.NewGuid(), null), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public void RefundPaymentCommand_IsAuditableForPaymentRefunded()
    {
        Guid paymentId = Guid.NewGuid();
        IAuditableCommand command = new RefundPaymentCommand(paymentId, null);

        command.AuditAction.Should().Be("Payment.Refunded");
        command.AuditTargetType.Should().Be("Payment");
        command.AuditTargetId.Should().Be(paymentId);
    }

    // ── G-14 / G-13 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_FullRefund_MarksTheAppointmentsDepositRefunded()
    {
        // G-14: the client's My bookings read "Deposit paid" after a full refund.
        await SeedStudio();
        (Guid paymentId, Guid appointmentId) = await SeedPaymentWithAppointment(1234m);

        await CreateSut().Handle(new RefundPaymentCommand(paymentId, null), default);

        _db.Appointments.Single(a => a.Id == appointmentId).DepositStatus.Should().Be(DepositStatus.Refunded);
    }

    [Fact]
    public async Task Handle_PartialRefund_LeavesTheDepositPaid_BecauseTheStudioStillHoldsPartOfIt()
    {
        await SeedStudio();
        (Guid paymentId, Guid appointmentId) = await SeedPaymentWithAppointment(1234m);

        await CreateSut().Handle(new RefundPaymentCommand(paymentId, 234m), default);

        _db.Appointments.Single(a => a.Id == appointmentId).DepositStatus.Should().Be(DepositStatus.Paid);
    }

    [Fact]
    public async Task Handle_Response_CarriesTheRefundedAmountAndAppointmentStatus()
    {
        // G-13: the screens need these to show "1,000 kept · 234 refunded".
        await SeedStudio();
        (Guid paymentId, _) = await SeedPaymentWithAppointment(1234m);

        PaymentResponse result = await CreateSut().Handle(new RefundPaymentCommand(paymentId, 234m), default);

        result.RefundedAmount.Should().Be(234m);
        result.AppointmentStatus.Should().Be(AppointmentStatus.Confirmed.ToString());
    }

    // ── C-14: several partial refunds ─────────────────────────────────────────

    [Fact]
    public async Task Handle_SecondPartialRefund_IsAllowed_AndTheRefundedTotalAddsUp()
    {
        // POK allows several partial refunds (staging, 2026-10-16); our app allowed only one.
        await SeedStudio();
        (Guid paymentId, Guid appointmentId) = await SeedPaymentWithAppointment(1234m);

        await CreateSut().Handle(new RefundPaymentCommand(paymentId, 234m), default);
        PaymentResponse second = await CreateSut().Handle(new RefundPaymentCommand(paymentId, 500m), default);

        second.RefundedAmount.Should().Be(734m);
        _db.Payments.Single(p => p.Id == paymentId).RefundedAmount.Should().Be(734m);
        await _stripe.Received(1).RefundAsync(_studioId, "order-1", 500m, "ALL", Arg.Any<CancellationToken>());
        _db.Appointments.Single(a => a.Id == appointmentId).DepositStatus.Should().Be(DepositStatus.Paid);
    }

    [Fact]
    public async Task Handle_RefundWithoutAmountAfterAPartialOne_RefundsWhatIsLeft_AndMarksTheDepositRefunded()
    {
        await SeedStudio();
        (Guid paymentId, Guid appointmentId) = await SeedPaymentWithAppointment(1234m);

        await CreateSut().Handle(new RefundPaymentCommand(paymentId, 234m), default);
        await CreateSut().Handle(new RefundPaymentCommand(paymentId, null), default);

        await _stripe.Received(1).RefundAsync(_studioId, "order-1", 1000m, "ALL", Arg.Any<CancellationToken>());
        _db.Payments.Single(p => p.Id == paymentId).RefundedAmount.Should().Be(1234m);
        _db.Appointments.Single(a => a.Id == appointmentId).DepositStatus.Should().Be(DepositStatus.Refunded);
    }

    [Fact]
    public async Task Handle_SecondRefundMoreThanWhatIsLeft_IsRefused_WithoutCallingThePaymentProvider()
    {
        await SeedStudio();
        (Guid paymentId, _) = await SeedPaymentWithAppointment(1234m);
        await CreateSut().Handle(new RefundPaymentCommand(paymentId, 234m), default);
        _stripe.ClearReceivedCalls();

        Func<Task> act = () => CreateSut().Handle(new RefundPaymentCommand(paymentId, 1001m), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>().WithMessage("*left to refund*");
        await _stripe.DidNotReceive().RefundAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.Payments.Single(p => p.Id == paymentId).RefundedAmount.Should().Be(234m);
    }

    [Fact]
    public async Task Handle_FullyRefundedPayment_CannotBeRefundedAgain()
    {
        await SeedStudio();
        (Guid paymentId, _) = await SeedPaymentWithAppointment(1234m);
        await CreateSut().Handle(new RefundPaymentCommand(paymentId, null), default);

        Func<Task> act = () => CreateSut().Handle(new RefundPaymentCommand(paymentId, 1m), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>().WithMessage("*left to refund*");
    }

    [Fact]
    public async Task Handle_RefundedPaymentFromBeforeRefundedAmountExisted_CountsAsFullyRefunded()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(200m, PaymentStatus.Refunded, "pi_old"); // RefundedAmount null

        Func<Task> act = () => CreateSut().Handle(new RefundPaymentCommand(paymentId, 10m), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
        await _stripe.DidNotReceive().RefundAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TellsTheClientTheAmountOfThisRefund_NotThePaymentsWholeAmount()
    {
        // Before C-14 the email/SMS always said the full amount ("a refund of ALL 1,234").
        await SeedStudio();
        (Guid paymentId, _) = await SeedPaymentWithAppointment(1234m);

        await CreateSut().Handle(new RefundPaymentCommand(paymentId, 234m), default);

        await _sender.Received(1).Send(
            Arg.Is<SendPaymentRefundedNotificationCommand>(c => c.PaymentId == paymentId && c.RefundAmount == 234m),
            Arg.Any<CancellationToken>());
    }

    private async Task<(Guid PaymentId, Guid AppointmentId)> SeedPaymentWithAppointment(decimal amount)
    {
        Client client = new()
        {
            StudioId = _studioId,
            FirstName = "Test",
            LastName = "Client",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ClientId = client.Id,
            Date = DateTime.UtcNow.Date.AddDays(5).AddHours(9),
            EndDate = DateTime.UtcNow.Date.AddDays(5).AddHours(10),
            DurationMinutes = 60,
            Status = AppointmentStatus.Confirmed,
            DepositAmount = amount,
            DepositStatus = DepositStatus.Paid,
        };
        _db.Appointments.Add(appointment);
        Payment payment = new()
        {
            StudioId = _studioId,
            AppointmentId = appointment.Id,
            ClientId = client.Id,
            Amount = amount,
            Currency = "ALL",
            Status = PaymentStatus.Paid,
            ProviderReferenceId = "order-1",
            PaidAt = DateTime.UtcNow,
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return (payment.Id, appointment.Id);
    }

    [Fact]
    public async Task Handle_PartialRefund_LeavesTheFrozenLekReferenceUntouched()
    {
        // Spec D5: a refund's lek figure is the refunded euros x the ORIGINAL rate, computed at display
        // time; the stored reference itself never changes after payment.
        await SeedStudio();
        Guid paymentId = await SeedPayment(200m, PaymentStatus.Paid, "pi_test");
        Payment seeded = _db.Payments.Single(p => p.Id == paymentId);
        seeded.ReferenceCurrency = "ALL";
        seeded.ReferenceRate = 90.74m;
        seeded.ReferenceRateDate = new DateOnly(2026, 10, 23);
        seeded.ReferenceRateSource = "BoA-Fix";
        seeded.ReferenceAmount = 18148m;
        await _db.SaveChangesAsync();

        PaymentResponse result = await CreateSut().Handle(new RefundPaymentCommand(paymentId, 50m), default);

        Payment stored = _db.Payments.Single(p => p.Id == paymentId);
        stored.ReferenceRate.Should().Be(90.74m);
        stored.ReferenceAmount.Should().Be(18148m);
        stored.ReferenceRateDate.Should().Be(new DateOnly(2026, 10, 23));
        result.ReferenceRate.Should().Be(90.74m);
        result.RefundedAmount.Should().Be(50m);
    }

    private async Task SeedStudio()
    {
        _db.Studios.Add(new Studio
        {
            Id = _studioId,
            Name = "Test",
            Slug = "test"
        });
        await _db.SaveChangesAsync();
    }

    private async Task<Guid> SeedPayment(decimal amount, PaymentStatus status, string? stripeIntentId)
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

        Payment payment = new()
        {
            StudioId = _studioId,
            AppointmentId = Guid.NewGuid(),
            ClientId = client.Id,
            Amount = amount,
            Currency = "EUR",
            Status = status,
            ProviderReferenceId = stripeIntentId
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment.Id;
    }
}
