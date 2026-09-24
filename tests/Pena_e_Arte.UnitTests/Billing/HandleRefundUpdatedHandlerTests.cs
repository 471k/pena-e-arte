using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class HandleRefundUpdatedHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private HandleRefundUpdatedHandler CreateSut() => new(_db, NullLogger<HandleRefundUpdatedHandler>.Instance);

    [Fact]
    public async Task Handle_SucceededEvent_SetsStatusSucceeded()
    {
        Guid refundId = await Seed("re_succ", RefundStatus.Pending);

        await CreateSut().Handle(new HandleRefundUpdatedCommand("re_succ", "succeeded", null), default);

        _db.SubscriptionRefunds.Single(r => r.Id == refundId).Status.Should().Be(RefundStatus.Succeeded);
    }

    [Fact]
    public async Task Handle_FailedEvent_SetsStatusFailed_AndFailureReason()
    {
        Guid refundId = await Seed("re_fail", RefundStatus.Pending);

        await CreateSut().Handle(new HandleRefundUpdatedCommand("re_fail", "failed", "insufficient_funds"), default);

        SubscriptionRefund refund = _db.SubscriptionRefunds.Single(r => r.Id == refundId);
        refund.Status.Should().Be(RefundStatus.Failed);
        refund.FailureReason.Should().Be("insufficient_funds");
    }

    [Fact]
    public async Task Handle_CanceledEvent_SetsStatusFailed()
    {
        Guid refundId = await Seed("re_cancel", RefundStatus.Pending);

        await CreateSut().Handle(new HandleRefundUpdatedCommand("re_cancel", "canceled", null), default);

        _db.SubscriptionRefunds.Single(r => r.Id == refundId).Status.Should().Be(RefundStatus.Failed);
    }

    [Fact]
    public async Task Handle_UnknownRefundId_DoesNotThrow()
    {
        Func<Task> act = () => CreateSut().Handle(new HandleRefundUpdatedCommand("re_unknown", "succeeded", null), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_SameEventTwice_Idempotent_NoDoubleProcessing()
    {
        Guid refundId = await Seed("re_dup", RefundStatus.Pending);

        HandleRefundUpdatedCommand command = new("re_dup", "succeeded", null);
        await CreateSut().Handle(command, default);
        await CreateSut().Handle(command, default);

        _db.SubscriptionRefunds.Count(r => r.Id == refundId).Should().Be(1);
        _db.SubscriptionRefunds.Single(r => r.Id == refundId).Status.Should().Be(RefundStatus.Succeeded);
    }

    private async Task<Guid> Seed(string stripeRefundId, RefundStatus status)
    {
        SubscriptionRefund refund = new()
        {
            SubscriptionId = Guid.NewGuid(),
            StudioId = Guid.NewGuid(),
            SubscriptionInvoicePaymentId = Guid.NewGuid(),
            StripeRefundId = stripeRefundId,
            StripeInvoiceId = "in_1",
            Amount = 100m,
            Currency = "eur",
            Rule = RefundRule.YearlyFormula,
            InitiatedByUserId = Guid.NewGuid(),
            Status = status,
        };
        _db.SubscriptionRefunds.Add(refund);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return refund.Id;
    }
}
