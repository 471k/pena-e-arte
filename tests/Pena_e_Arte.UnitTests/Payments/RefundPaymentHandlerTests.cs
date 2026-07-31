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
                Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
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
            "pi_test", 20000L, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PartialRefund_PassesCorrectAmountToStripe()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(200m, PaymentStatus.Paid, "pi_test");

        await CreateSut().Handle(new RefundPaymentCommand(paymentId, 50m), default);

        await _stripe.Received(1).RefundAsync(
            "pi_test", 5000L, Arg.Any<CancellationToken>());
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
    public async Task Handle_PaymentNotFound_ThrowsNotFoundException()
    {
        await SeedStudio();

        Func<Task> act = () => CreateSut().Handle(new RefundPaymentCommand(Guid.NewGuid(), null), default);

        await act.Should().ThrowAsync<NotFoundException>();
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
            Status = status,
            ProviderReferenceId = stripeIntentId
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment.Id;
    }
}
