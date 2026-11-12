using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Billing;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Application.Billing.Queries;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Contracts.Requests;
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

        SubscriptionResponse result = await CreateSut().Handle(await Confirmed(), default);

        result.Status.Should().Be(SubscriptionStatus.Cancelled.ToString());
        _db.SubscriptionRefunds.Single(r => r.SubscriptionId == subId).Amount.Should().Be(553m); // 790 - 3*79
        await _billing.Received(1).CancelSubscriptionAsync(stripeId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_YearlyWithNoInvoice_ZeroRefund_StillCancels()
    {
        const string stripeId = "sub_yearly_noinvoice";
        await SeedSubscription(BillingInterval.Yearly, SubscriptionStatus.Active, stripeId);

        SubscriptionResponse result = await CreateSut().Handle(await Confirmed(), default);

        result.Status.Should().Be(SubscriptionStatus.Cancelled.ToString());
        _db.SubscriptionRefunds.Any().Should().BeFalse();
    }

    [Fact]
    public async Task Handle_Monthly_SchedulesCancellation_StatusStaysActive_CancelAtPeriodEndTrue()
    {
        const string stripeId = "sub_monthly";
        await SeedSubscription(BillingInterval.Monthly, SubscriptionStatus.Active, stripeId);

        SubscriptionResponse result = await CreateSut().Handle(await Confirmed(), default);

        result.Status.Should().Be(SubscriptionStatus.Active.ToString());
        result.CancelAtPeriodEnd.Should().BeTrue();
        await _billing.Received(1).ScheduleCancellationAsync(stripeId, Arg.Any<CancellationToken>());
        await _billing.DidNotReceive().CancelSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CashBilled_ImmediateCancel_NoStripeCalls()
    {
        await SeedSubscription(BillingInterval.Monthly, SubscriptionStatus.Active, stripeId: null);

        SubscriptionResponse result = await CreateSut().Handle(await Confirmed(), default);

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

        await CreateSut().Handle(await Confirmed(), default);

        await _billing.Received(1).CancelScheduledPriceChangeAsync(stripeId, Arg.Any<CancellationToken>());
        _db.Subscriptions.Single(s => s.StudioId == _studioId).PendingPlanId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AlreadyCancelledStatus_Throws()
    {
        await SeedSubscription(BillingInterval.Monthly, SubscriptionStatus.Cancelled, "sub_x");

        CancelMySubscriptionCommand command = await Confirmed();
        Func<Task> act = () => CreateSut().Handle(command, default);

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
        CancelMySubscriptionCommand command = await Confirmed();
        await CreateSut().Handle(command, default);

        // A retry re-fetches the (already-cancelled) subscription and re-runs Handle —
        // status is no longer cancellable, so it throws before reaching the refund branch
        // again; the idempotency guard inside YearlyRefundIssuer is the second, independent
        // line of defense for a true network-retry-before-save race (§2.6/A3), verified here
        // by asserting the refund was issued exactly once regardless.
        Func<Task> act = () => CreateSut().Handle(command, default);
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

        await CreateSut().Handle(await Confirmed(), default);

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

        await CreateSut().Handle(await Confirmed(), default);

        _db.SubscriptionRevenueEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CashBilled_WritesChurnImmediately_NoWebhookWillEverFire()
    {
        await SeedSubscription(BillingInterval.Monthly, SubscriptionStatus.Active, stripeId: null);
        await SetBilled(59m);

        await CreateSut().Handle(await Confirmed(), default);

        SubscriptionRevenueEvent ledgerEvent = _db.SubscriptionRevenueEvents.Single();
        ledgerEvent.Type.Should().Be(RevenueEventType.Churn);
        ledgerEvent.MrrBefore.Should().Be(59m);
        ledgerEvent.MrrAfter.Should().Be(0m);
    }

    // ── E05b-2 (O07): the cancellation is bound to the quote the owner confirmed ──────────

    /// <summary>What the dialog does: fetch the quote from the real quote handler, then confirm exactly it.</summary>
    private async Task<CancelMySubscriptionCommand> Confirmed()
    {
        ICurrentTenant tenant = Substitute.For<ICurrentTenant>();
        tenant.StudioId.Returns(_studioId);
        CancellationQuoteResponse quote = await new GetCancellationQuoteHandler(_db, tenant)
            .Handle(new GetCancellationQuoteQuery(), default);
        _db.ChangeTracker.Clear();
        return new CancelMySubscriptionCommand(_studioId, AsConfirmation(quote));
    }

    private static CancelMySubscriptionRequest AsConfirmation(CancellationQuoteResponse q) =>
        new(q.BillingInterval, q.RefundAmount, q.MonthsUsed, q.AmountPaid, q.InvoiceId);

    /// <summary>The quote as it was a month ago: the owner opened the dialog before a month boundary.</summary>
    private CancelMySubscriptionCommand QuotedAMonthAgo()
    {
        Subscription sub = _db.Subscriptions.Single(s => s.StudioId == _studioId);
        SubscriptionInvoicePayment invoice = _db.SubscriptionInvoicePayments.Single(i => i.SubscriptionId == sub.Id);
        CancellationQuoteResponse old = CancellationQuotes.Build(sub, invoice, DateTime.UtcNow.AddMonths(-1)).Response;
        _db.ChangeTracker.Clear();
        return new CancelMySubscriptionCommand(_studioId, AsConfirmation(old));
    }

    private async Task AssertNothingCancelled()
    {
        _billing.ReceivedCalls().Should().BeEmpty("a stale quote must stop before any billing call");
        _db.SubscriptionRefunds.Should().BeEmpty();
        _db.SubscriptionRevenueEvents.Should().BeEmpty();
        await _sender.DidNotReceive().Send(Arg.Any<SendSubscriptionCancelledNotificationCommand>(), Arg.Any<CancellationToken>());
        Subscription sub = _db.Subscriptions.Single(s => s.StudioId == _studioId);
        sub.Status.Should().Be(SubscriptionStatus.Active);
        sub.CancelAtPeriodEnd.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_QuoteFromBeforeAMonthBoundary_IsRefused_AndNothingReachesBilling()
    {
        await SeedYearly("sub_stale", amountPaid: 790m, monthlyRef: 79m, periodStartMonthsAgo: 3);
        CancelMySubscriptionCommand stale = QuotedAMonthAgo();
        stale.ConfirmedQuote.MonthsUsed.Should().Be(2, "the seed is 3 months in, so a month ago 2 months were used");
        stale.ConfirmedQuote.RefundAmount.Should().Be(632m);

        Func<Task> act = () => CreateSut().Handle(stale, default);

        await act.Should().ThrowAsync<CancellationQuoteChangedException>();
        await AssertNothingCancelled();
        stale.ComputedRefundAmount.Should().BeNull("nothing was refunded, so there is nothing to audit");
    }

    [Fact]
    public async Task Handle_StaleQuote_WithAScheduledDowngrade_DoesNotEvenReleaseTheDowngrade()
    {
        await SeedYearly("sub_stale_pending", amountPaid: 790m, monthlyRef: 79m, periodStartMonthsAgo: 3);
        Subscription sub = _db.Subscriptions.Single(s => s.StudioId == _studioId);
        sub.PendingPlanId = Guid.NewGuid();
        sub.PendingBillingInterval = BillingInterval.Monthly;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        Func<Task> act = () => CreateSut().Handle(QuotedAMonthAgo(), default);

        await act.Should().ThrowAsync<CancellationQuoteChangedException>();
        _billing.ReceivedCalls().Should().BeEmpty();
        _db.Subscriptions.Single(s => s.StudioId == _studioId).PendingPlanId.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_QuoteForADifferentInvoice_IsRefused()
    {
        await SeedYearly("sub_new_invoice", amountPaid: 790m, monthlyRef: 79m, periodStartMonthsAgo: 3);
        CancelMySubscriptionCommand confirmed = await Confirmed();
        CancelMySubscriptionCommand otherInvoice = confirmed with
        {
            ConfirmedQuote = confirmed.ConfirmedQuote with { InvoiceId = Guid.NewGuid() },
        };

        Func<Task> act = () => CreateSut().Handle(otherInvoice, default);

        await act.Should().ThrowAsync<CancellationQuoteChangedException>();
        await AssertNothingCancelled();
    }

    [Fact]
    public async Task Handle_MonthlyQuote_ForAPlanThatIsNowYearly_IsRefused()
    {
        await SeedYearly("sub_switched", amountPaid: 790m, monthlyRef: 79m, periodStartMonthsAgo: 3);
        CancelMySubscriptionCommand monthly = new(_studioId, new CancelMySubscriptionRequest("Monthly", 0m, null, null, null));

        Func<Task> act = () => CreateSut().Handle(monthly, default);

        await act.Should().ThrowAsync<CancellationQuoteChangedException>();
        await AssertNothingCancelled();
    }

    [Fact]
    public async Task Handle_MatchingQuote_StillFillsTheAuditMetadataMrrRulesReads()
    {
        await SeedYearly("sub_audit", amountPaid: 790m, monthlyRef: 79m, periodStartMonthsAgo: 3);
        CancelMySubscriptionCommand command = await Confirmed();

        await CreateSut().Handle(command, default);

        command.ComputedRefundAmount.Should().Be(553m);
        command.ComputedMonthsUsed.Should().Be(3);
        string metadata = AuditMetadataBuilder.Build(command);
        metadata.Should().Contain("553").And.Contain("monthsUsed");
    }

    [Fact]
    public void Validator_RefusesACancellationWithoutAQuote()
    {
        CancelMySubscriptionValidator validator = new();

        validator.Validate(new CancelMySubscriptionCommand(_studioId, null!)).IsValid.Should().BeFalse();
        validator.Validate(new CancelMySubscriptionCommand(_studioId, new CancelMySubscriptionRequest("Weekly", 0m, null, null, null)))
            .IsValid.Should().BeFalse();
        validator.Validate(new CancelMySubscriptionCommand(_studioId, new CancelMySubscriptionRequest("Monthly", 0m, null, null, null)))
            .IsValid.Should().BeTrue();
    }
}
