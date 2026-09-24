using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class CancelMySubscriptionHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IStripeBillingService _billing = Substitute.For<IStripeBillingService>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly Guid _studioId = Guid.NewGuid();

    public CancelMySubscriptionHandlerTests() => _currentUser.UserId.Returns(Guid.NewGuid());

    private CancelMySubscriptionHandler CreateSut() =>
        new(_db, _billing, _currentUser, _sender, NullLogger<CancelMySubscriptionHandler>.Instance);

    [Fact]
    public async Task Handle_YearlyWithSnapshottedInvoice_IssuesFormulaRefund_AndCancelsInStripe()
    {
        const string stripeId = "sub_yearly_ok";
        Guid subId = await SeedYearly(stripeId, amountPaid: 790m, monthlyRef: 79m, periodStartMonthsAgo: 3);

        SubscriptionResponse result = await CreateSut().Handle(new CancelMySubscriptionCommand(_studioId), default);

        result.Status.Should().Be(SubscriptionStatus.Cancelled.ToString());
        _db.SubscriptionRefunds.Single(r => r.SubscriptionId == subId).Amount.Should().Be(553m); // 790 - 3*79
        await _billing.Received(1).CancelSubscriptionAsync(stripeId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_YearlyWithNoInvoice_ZeroRefund_StillCancels()
    {
        const string stripeId = "sub_yearly_noinvoice";
        await SeedSubscription(BillingInterval.Yearly, SubscriptionStatus.Active, stripeId);

        SubscriptionResponse result = await CreateSut().Handle(new CancelMySubscriptionCommand(_studioId), default);

        result.Status.Should().Be(SubscriptionStatus.Cancelled.ToString());
        _db.SubscriptionRefunds.Any().Should().BeFalse();
    }

    [Fact]
    public async Task Handle_Monthly_SchedulesCancellation_StatusStaysActive_CancelAtPeriodEndTrue()
    {
        const string stripeId = "sub_monthly";
        await SeedSubscription(BillingInterval.Monthly, SubscriptionStatus.Active, stripeId);

        SubscriptionResponse result = await CreateSut().Handle(new CancelMySubscriptionCommand(_studioId), default);

        result.Status.Should().Be(SubscriptionStatus.Active.ToString());
        result.CancelAtPeriodEnd.Should().BeTrue();
        await _billing.Received(1).ScheduleCancellationAsync(stripeId, Arg.Any<CancellationToken>());
        await _billing.DidNotReceive().CancelSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CashBilled_ImmediateCancel_NoStripeCalls()
    {
        await SeedSubscription(BillingInterval.Monthly, SubscriptionStatus.Active, stripeId: null);

        SubscriptionResponse result = await CreateSut().Handle(new CancelMySubscriptionCommand(_studioId), default);

        result.Status.Should().Be(SubscriptionStatus.Cancelled.ToString());
        await _billing.DidNotReceive().CancelSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _billing.DidNotReceive().ScheduleCancellationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PendingPlanChange_CancelsScheduledPriceChangeFirst()
    {
        const string stripeId = "sub_pending";
        Guid planId = Guid.NewGuid();
        _db.Subscriptions.Add(new Subscription
        {
            StudioId = _studioId,
            Status = SubscriptionStatus.Active,
            BillingInterval = BillingInterval.Monthly,
            StripeSubscriptionId = stripeId,
            PendingPlanId = planId,
            PendingBillingInterval = BillingInterval.Monthly,
            TrialExpiresAt = null,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(10),
            GracePeriodEnd = DateTime.UtcNow.AddDays(-13),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateSut().Handle(new CancelMySubscriptionCommand(_studioId), default);

        await _billing.Received(1).CancelScheduledPriceChangeAsync(stripeId, Arg.Any<CancellationToken>());
        _db.Subscriptions.Single(s => s.StudioId == _studioId).PendingPlanId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AlreadyCancelledStatus_Throws()
    {
        await SeedSubscription(BillingInterval.Monthly, SubscriptionStatus.Cancelled, "sub_x");

        Func<Task> act = () => CreateSut().Handle(new CancelMySubscriptionCommand(_studioId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_DoubleCancel_OnlyOneRefundRow_RefundAsyncCalledOnce()
    {
        const string stripeId = "sub_double";
        await SeedYearly(stripeId, amountPaid: 790m, monthlyRef: 79m, periodStartMonthsAgo: 3);

        _billing.RefundAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("re_1", "succeeded"));

        // First cancel: subscription flips to Cancelled, refund issued.
        await CreateSut().Handle(new CancelMySubscriptionCommand(_studioId), default);

        // A retry re-fetches the (already-cancelled) subscription and re-runs Handle —
        // status is no longer cancellable, so it throws before reaching the refund branch
        // again; the idempotency guard inside YearlyRefundIssuer is the second, independent
        // line of defense for a true network-retry-before-save race (§2.6/A3), verified here
        // by asserting the refund was issued exactly once regardless.
        Func<Task> act = () => CreateSut().Handle(new CancelMySubscriptionCommand(_studioId), default);
        await act.Should().ThrowAsync<BusinessRuleViolationException>();

        _db.SubscriptionRefunds.Count().Should().Be(1);
        await _billing.Received(1).RefundAsync(
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private async Task<Guid> SeedYearly(string stripeId, decimal amountPaid, decimal monthlyRef, int periodStartMonthsAgo)
    {
        Subscription sub = new()
        {
            StudioId = _studioId,
            Status = SubscriptionStatus.Active,
            BillingInterval = BillingInterval.Yearly,
            StripeSubscriptionId = stripeId,
            TrialExpiresAt = null,
            CurrentPeriodEnd = DateTime.UtcNow.AddMonths(12 - periodStartMonthsAgo),
            GracePeriodEnd = DateTime.UtcNow.AddDays(-13),
        };
        _db.Subscriptions.Add(sub);
        await _db.SaveChangesAsync();

        _db.SubscriptionInvoicePayments.Add(new SubscriptionInvoicePayment
        {
            SubscriptionId = sub.Id,
            StudioId = _studioId,
            StripeInvoiceId = $"in_{stripeId}",
            AmountPaid = amountPaid,
            Currency = "eur",
            // +5 minutes: guards against the exact-anniversary boundary — without this
            // buffer, the few milliseconds of real time between seeding "now" and the
            // handler's own DateTime.UtcNow.Now can tip MonthsUsed's monthly-anniversary
            // loop into counting one extra started month than intended.
            PaidAt = DateTime.UtcNow.AddMonths(-periodStartMonthsAgo).AddMinutes(5),
            PeriodStart = DateTime.UtcNow.AddMonths(-periodStartMonthsAgo).AddMinutes(5),
            MonthlyReferencePrice = monthlyRef,
            StripePaymentIntentId = $"pi_{stripeId}",
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return sub.Id;
    }

    private async Task SeedSubscription(BillingInterval interval, SubscriptionStatus status, string? stripeId)
    {
        _db.Subscriptions.Add(new Subscription
        {
            StudioId = _studioId,
            Status = status,
            BillingInterval = interval,
            StripeSubscriptionId = stripeId,
            TrialExpiresAt = null,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(10),
            GracePeriodEnd = DateTime.UtcNow.AddDays(-13),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    // ── Revenue ledger ────────────────────────────────────────────────────

    private async Task SetBilled(decimal amount)
    {
        Subscription sub = _db.Subscriptions.Single(s => s.StudioId == _studioId);
        sub.BilledUnitAmount = amount;
        sub.BilledQuantity = 1;
        sub.BilledCurrency = "eur";
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Handle_Yearly_WritesChurnOfMonthlyEquivalent()
    {
        await SeedYearly("sub_yearly_ledger", amountPaid: 590m, monthlyRef: 59m, periodStartMonthsAgo: 3);
        await SetBilled(590m);

        await CreateSut().Handle(new CancelMySubscriptionCommand(_studioId), default);

        SubscriptionRevenueEvent ledgerEvent = _db.SubscriptionRevenueEvents.Single();
        ledgerEvent.Type.Should().Be(RevenueEventType.Churn);
        ledgerEvent.MrrBefore.Should().BeApproximately(49.17m, 0.01m); // 590 / 12
        ledgerEvent.MrrAfter.Should().Be(0m);
        ledgerEvent.Source.Should().Be(nameof(CancelMySubscriptionHandler));
    }

    [Fact]
    public async Task Handle_MonthlyCardBilled_WritesNoChurnYet_StripeDeletedWebhookWillDoIt()
    {
        await SeedSubscription(BillingInterval.Monthly, SubscriptionStatus.Active, "sub_monthly_ledger");
        await SetBilled(59m);

        await CreateSut().Handle(new CancelMySubscriptionCommand(_studioId), default);

        _db.SubscriptionRevenueEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CashBilled_WritesChurnImmediately_NoWebhookWillEverFire()
    {
        await SeedSubscription(BillingInterval.Monthly, SubscriptionStatus.Active, stripeId: null);
        await SetBilled(59m);

        await CreateSut().Handle(new CancelMySubscriptionCommand(_studioId), default);

        SubscriptionRevenueEvent ledgerEvent = _db.SubscriptionRevenueEvents.Single();
        ledgerEvent.Type.Should().Be(RevenueEventType.Churn);
        ledgerEvent.MrrBefore.Should().Be(59m);
        ledgerEvent.MrrAfter.Should().Be(0m);
    }
}
