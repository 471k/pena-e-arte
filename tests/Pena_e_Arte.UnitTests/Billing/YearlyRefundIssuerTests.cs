using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Billing;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

/// <summary>Directly exercises YearlyRefundIssuer's own idempotency guard (A3 — "cancel sent
/// twice / network retry: exactly one Stripe refund"), independent of either cancel handler's
/// own status-guard, which is the more common real-world path but doesn't reach this code a
/// second time. This proves the DB-level guarantee the unique index backs.</summary>
public class YearlyRefundIssuerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IStripeBillingService _billing = Substitute.For<IStripeBillingService>();

    [Fact]
    public async Task IssueAsync_CalledTwiceForSameInvoice_OnlyOneRefundRow_RefundAsyncCalledOnce()
    {
        _billing.RefundAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("re_1", "succeeded"));

        Subscription sub = new()
        {
            StudioId = Guid.NewGuid(),
            Status = SubscriptionStatus.Cancelled,
            BillingInterval = BillingInterval.Yearly,
            StripeSubscriptionId = "sub_idempotent",
            TrialExpiresAt = null,
            CurrentPeriodEnd = DateTime.UtcNow,
            GracePeriodEnd = DateTime.UtcNow.AddDays(-13),
        };
        _db.Subscriptions.Add(sub);
        await _db.SaveChangesAsync();

        SubscriptionInvoicePayment invoice = new()
        {
            SubscriptionId = sub.Id,
            StudioId = sub.StudioId,
            StripeInvoiceId = "in_idempotent",
            AmountPaid = 790m,
            Currency = "eur",
            PaidAt = DateTime.UtcNow.AddMonths(-3),
            PeriodStart = DateTime.UtcNow.AddMonths(-3),
            MonthlyReferencePrice = 79m,
            StripePaymentIntentId = "pi_idempotent",
        };
        _db.SubscriptionInvoicePayments.Add(invoice);
        await _db.SaveChangesAsync();

        YearlyRefundQuote quote = new(3, 790m, 79m, 553m);

        await YearlyRefundIssuer.IssueAsync(
            _db, _billing, sub, invoice, quote, RefundRule.YearlyFormula,
            Guid.NewGuid(), null, NullLogger.Instance, default);
        await _db.SaveChangesAsync();

        // Second call against the exact same invoice — simulates a network retry.
        await YearlyRefundIssuer.IssueAsync(
            _db, _billing, sub, invoice, quote, RefundRule.YearlyFormula,
            Guid.NewGuid(), null, NullLogger.Instance, default);
        await _db.SaveChangesAsync();

        _db.SubscriptionRefunds.Count(r => r.SubscriptionInvoicePaymentId == invoice.Id).Should().Be(1);
        await _billing.Received(1).RefundAsync(
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
