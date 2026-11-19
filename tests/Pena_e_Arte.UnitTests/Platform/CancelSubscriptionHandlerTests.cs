using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Application.Platform.Commands;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

public class CancelSubscriptionHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IStripeBillingService _stripe = Substitute.For<IStripeBillingService>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    public CancelSubscriptionHandlerTests()
    {
        _currentUser.UserId.Returns(Guid.NewGuid());
    }

    private CancelSubscriptionHandler CreateSut() =>
        new(_db, _stripe, _currentUser, TimeProvider.System, NullLogger<CancelSubscriptionHandler>.Instance);

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Trialing)]
    [InlineData(SubscriptionStatus.GracePeriod)]
    public async Task Handle_CancellableStatus_SetsStatusToCancelled(SubscriptionStatus status)
    {
        Guid studioId = await SeedStudio(status, stripeId: null);

        await CreateSut().Handle(await Confirmed(studioId), default);

        _db.Subscriptions.Single(s => s.StudioId == studioId)
            .Status.Should().Be(SubscriptionStatus.Cancelled);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.PastDue)]
    public async Task Handle_WithStripeSubscriptionId_CallsStripeCancellation(SubscriptionStatus status)
    {
        const string stripeId = "sub_test_123";
        Guid studioId = await SeedStudio(status, stripeId: stripeId);

        await CreateSut().Handle(await Confirmed(studioId), default);

        await _stripe.Received(1).CancelSubscriptionAsync(stripeId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithoutStripeSubscriptionId_DoesNotCallStripe()
    {
        Guid studioId = await SeedStudio(SubscriptionStatus.Active, stripeId: null);

        await CreateSut().Handle(await Confirmed(studioId), default);

        await _stripe.DidNotReceive().CancelSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StripeCallFails_DoesNotThrow_AndLocalRecordIsStillCancelled()
    {
        const string stripeId = "sub_fail_456";
        Guid studioId = await SeedStudio(SubscriptionStatus.Active, stripeId: stripeId);

        _stripe.CancelSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .ThrowsAsync(new Exception("Stripe timeout"));

        CancelSubscriptionCommand command = await Confirmed(studioId);
        Func<Task> act = () => CreateSut().Handle(command, default);

        await act.Should().NotThrowAsync();
        _db.Subscriptions.Single(s => s.StudioId == studioId)
            .Status.Should().Be(SubscriptionStatus.Cancelled);
    }

    [Fact]
    public async Task Handle_ClearsPendingPlanId()
    {
        Guid planId = Guid.NewGuid();
        Guid studioId = await SeedStudio(SubscriptionStatus.Active, stripeId: null, pendingPlanId: planId);

        await CreateSut().Handle(await Confirmed(studioId), default);

        _db.Subscriptions.Single(s => s.StudioId == studioId)
            .PendingPlanId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_StudioNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new CancelSubscriptionCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_StudioWithNoSubscription_ThrowsBusinessRuleViolation()
    {
        _db.Studios.Add(new Studio
        {
            Id = Guid.NewGuid(),
            Name = "No-Sub Studio",
            Slug = "no-sub-cancel",
            TrialExpiresAt = DateTime.UtcNow.AddDays(7),
        });
        await _db.SaveChangesAsync();
        Guid studioId = _db.Studios.First(s => s.Slug == "no-sub-cancel").Id;
        _db.ChangeTracker.Clear();

        CancelSubscriptionCommand command = await Confirmed(studioId);
        Func<Task> act = () => CreateSut().Handle(command, default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Theory]
    [InlineData(SubscriptionStatus.Cancelled)]
    public async Task Handle_AlreadyCancelledStatus_ThrowsBusinessRuleViolation(SubscriptionStatus status)
    {
        Guid studioId = await SeedStudio(status, stripeId: null);

        CancelSubscriptionCommand command = await Confirmed(studioId);
        Func<Task> act = () => CreateSut().Handle(command, default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    // --- §2.5 admin refund override ---

    [Fact]
    public async Task Handle_YearlyWithInvoice_NoOverride_UsesFormula()
    {
        const string stripeId = "sub_yearly_formula";
        (Guid studioId, Guid subId) = await SeedYearlyStudioWithInvoice(stripeId, amountPaid: 790m, monthlyRef: 79m, monthsAgo: 3);

        await CreateSut().Handle(await Confirmed(studioId), default);

        SubscriptionRefund refund = _db.SubscriptionRefunds.Single(r => r.SubscriptionId == subId);
        refund.Rule.Should().Be(RefundRule.YearlyFormula);
        refund.Amount.Should().Be(553m); // 790 - 3*79
    }

    [Fact]
    public async Task Handle_YearlyWithInvoice_AdminFull_RefundsFullAmountPaid_RegardlessOfMonthsUsed()
    {
        const string stripeId = "sub_yearly_full";
        (Guid studioId, Guid subId) = await SeedYearlyStudioWithInvoice(stripeId, amountPaid: 790m, monthlyRef: 79m, monthsAgo: 11);

        await CreateSut().Handle(
            await Confirmed(studioId, "AdminFull", "Goodwill"), default);

        SubscriptionRefund refund = _db.SubscriptionRefunds.Single(r => r.SubscriptionId == subId);
        refund.Rule.Should().Be(RefundRule.AdminFull);
        refund.Amount.Should().Be(790m);
        refund.AdminReason.Should().Be("Goodwill");
    }

    [Fact]
    public async Task Handle_YearlyWithInvoice_AdminNone_RecordsZeroAmountSucceededRow_NoStripeRefundCall()
    {
        const string stripeId = "sub_yearly_none";
        (Guid studioId, Guid subId) = await SeedYearlyStudioWithInvoice(stripeId, amountPaid: 790m, monthlyRef: 79m, monthsAgo: 1);

        await CreateSut().Handle(
            await Confirmed(studioId, "AdminNone", "Policy violation"), default);

        SubscriptionRefund refund = _db.SubscriptionRefunds.Single(r => r.SubscriptionId == subId);
        refund.Rule.Should().Be(RefundRule.AdminNone);
        refund.Amount.Should().Be(0m);
        refund.Status.Should().Be(RefundStatus.Succeeded);
        await _stripe.DidNotReceive().RefundAsync(
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TrialingWithNoInvoice_OverrideIsNoOp_NoRefundRowCreated()
    {
        // §3 flag — Trialing/GracePeriod never have a paid yearly invoice, so Override must
        // not create a SubscriptionRefund row (not even a zero one) when there's nothing to
        // refund against.
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio
        {
            Id = studioId,
            Name = $"Studio-{studioId:N}"[..20],
            Slug = studioId.ToString("N")[..20],
            TrialExpiresAt = DateTime.UtcNow.AddDays(7),
        });
        _db.Subscriptions.Add(new Subscription
        {
            StudioId = studioId,
            Status = SubscriptionStatus.Trialing,
            BillingInterval = BillingInterval.Yearly,
            TrialExpiresAt = DateTime.UtcNow.AddDays(7),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
            StripeSubscriptionId = "sub_trialing_yearly",
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateSut().Handle(
            await Confirmed(studioId, "AdminFull", "n/a"), default);

        _db.SubscriptionRefunds.Any().Should().BeFalse();
    }

    // ── D03: the cancel carries the consequence the admin reviewed ─────────

    /// <summary>The command as the page sends it: with the confirmation built from the quote the admin would see now.
    /// Where the quote itself refuses (no subscription, not cancellable) the confirmation is a placeholder, because
    /// the handler refuses those before it compares anything.</summary>
    private async Task<CancelSubscriptionCommand> Confirmed(Guid studioId, string? overrideRule = null, string? reason = null)
    {
        AdminCancellationConfirmation confirmation;
        try
        {
            AdminCancellationQuoteResponse quote = await new GetAdminCancellationQuoteHandler(_db, TimeProvider.System)
                .Handle(new GetAdminCancellationQuoteQuery(studioId), default);
            decimal amount = overrideRule switch
            {
                "AdminFull" => quote.RefundApplies ? quote.FullRefund : 0m,
                "AdminNone" => 0m,
                _ => quote.FormulaRefund,
            };
            confirmation = new AdminCancellationConfirmation(quote.Status, quote.InvoiceId, amount);
        }
        catch (BusinessRuleViolationException)
        {
            confirmation = new AdminCancellationConfirmation("Active", null, 0m);
        }
        _db.ChangeTracker.Clear();
        return new CancelSubscriptionCommand(studioId, overrideRule, reason, confirmation);
    }

    [Fact]
    public async Task Handle_RefundChangedSinceTheQuote_Refuses_AndChangesNothing()
    {
        (Guid studioId, Guid subId) = await SeedYearlyStudioWithInvoice("sub_stale", amountPaid: 790m, monthlyRef: 79m, monthsAgo: 3);
        CancelSubscriptionCommand reviewed = await Confirmed(studioId);
        // The admin saw 553 but a month boundary (say) has passed: the server now computes 474.
        CancelSubscriptionCommand stale = reviewed with { Confirmed = reviewed.Confirmed! with { RefundAmount = 474m } };

        Func<Task> act = () => CreateSut().Handle(stale, default);

        (await act.Should().ThrowAsync<CancellationQuoteChangedException>())
            .WithMessage(CancelSubscriptionHandler.QuoteChangedMessage);
        _db.Subscriptions.Single(s => s.Id == subId).Status.Should().Be(SubscriptionStatus.Active);
        _db.SubscriptionRefunds.Should().BeEmpty();
        _db.SubscriptionRevenueEvents.Should().BeEmpty();
        await _stripe.DidNotReceive().CancelSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _stripe.DidNotReceive().RefundAsync(
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StatusChangedSinceTheQuote_Refuses()
    {
        // Reviewed while Trialing (nothing to refund, nothing billed); the studio has since activated.
        Guid studioId = await SeedStudio(SubscriptionStatus.Active, stripeId: "sub_status_moved");
        CancelSubscriptionCommand reviewed = await Confirmed(studioId);
        CancelSubscriptionCommand stale = reviewed with { Confirmed = reviewed.Confirmed! with { Status = "Trialing" } };

        Func<Task> act = () => CreateSut().Handle(stale, default);

        await act.Should().ThrowAsync<CancellationQuoteChangedException>();
        _db.Subscriptions.Single(s => s.StudioId == studioId).Status.Should().Be(SubscriptionStatus.Active);
        await _stripe.DidNotReceive().CancelSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RuleChangedWithoutAFreshReview_Refuses()
    {
        // Reviewed the formula (553), then sent "full" with the formula's figure: the amount for the rule sent is 790.
        (Guid studioId, _) = await SeedYearlyStudioWithInvoice("sub_rule_swap", amountPaid: 790m, monthlyRef: 79m, monthsAgo: 3);
        CancelSubscriptionCommand reviewed = await Confirmed(studioId);
        CancelSubscriptionCommand swapped = reviewed with { Override = "AdminFull", OverrideReason = "Goodwill" };

        Func<Task> act = () => CreateSut().Handle(swapped, default);

        await act.Should().ThrowAsync<CancellationQuoteChangedException>();
        _db.SubscriptionRefunds.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NoConfirmation_Refuses()
    {
        Guid studioId = await SeedStudio(SubscriptionStatus.Active, stripeId: null);

        Func<Task> act = () => CreateSut().Handle(new CancelSubscriptionCommand(studioId), default);

        await act.Should().ThrowAsync<CancellationQuoteChangedException>();
        _db.Subscriptions.Single(s => s.StudioId == studioId).Status.Should().Be(SubscriptionStatus.Active);
    }

    [Theory]
    [InlineData("1")]       // Enum.Parse would have accepted this as AdminFull
    [InlineData("Bogus")]
    [InlineData("YearlyFormula")]
    public async Task Handle_OverrideThatIsNotAnAdminRule_RefusesBeforeComparing(string overrideRule)
    {
        Guid studioId = await SeedStudio(SubscriptionStatus.Active, stripeId: null);
        CancelSubscriptionCommand command = (await Confirmed(studioId)) with { Override = overrideRule, OverrideReason = "x" };

        Func<Task> act = () => CreateSut().Handle(command, default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    private async Task<(Guid StudioId, Guid SubscriptionId)> SeedYearlyStudioWithInvoice(
        string stripeId, decimal amountPaid, decimal monthlyRef, int monthsAgo)
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio
        {
            Id = studioId,
            Name = $"Studio-{studioId:N}"[..20],
            Slug = studioId.ToString("N")[..20],
            TrialExpiresAt = DateTime.UtcNow.AddDays(-30),
        });
        Subscription sub = new()
        {
            StudioId = studioId,
            Status = SubscriptionStatus.Active,
            BillingInterval = BillingInterval.Yearly,
            TrialExpiresAt = null,
            CurrentPeriodEnd = DateTime.UtcNow.AddMonths(12 - monthsAgo),
            StripeSubscriptionId = stripeId,
        };
        _db.Subscriptions.Add(sub);
        await _db.SaveChangesAsync();

        _db.SubscriptionInvoicePayments.Add(new SubscriptionInvoicePayment
        {
            SubscriptionId = sub.Id,
            StudioId = studioId,
            StripeInvoiceId = $"in_{stripeId}",
            AmountPaid = amountPaid,
            Currency = "eur",
            // MonthsUsed is N (N = monthsAgo, at least 1) on any day. The old "AddMonths(-N) plus
            // 5 minutes" guarded against five minutes while AddMonths' month-end clamp moves an
            // anniversary by up to three days (31 Dec minus 3 months is 30 Sep, whose 3rd
            // anniversary is a day before now). The new seed keeps both neighbouring anniversaries
            // at least 12 days away, which the clamp can't reach. YearlyRefundCalculator itself is
            // correct; see CancelMySubscriptionHandlerTests.SeedYearly for the full check.
            PaidAt = DateTime.UtcNow.AddMonths(-(monthsAgo - 1)).AddDays(-15),
            PeriodStart = DateTime.UtcNow.AddMonths(-(monthsAgo - 1)).AddDays(-15),
            MonthlyReferencePrice = monthlyRef,
            StripePaymentIntentId = $"pi_{stripeId}",
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (studioId, sub.Id);
    }

    private async Task<Guid> SeedStudio(
        SubscriptionStatus status,
        string? stripeId,
        Guid? pendingPlanId = null)
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio
        {
            Id = studioId,
            Name = $"Studio-{studioId:N}"[..20],
            Slug = studioId.ToString("N")[..20],
            TrialExpiresAt = DateTime.UtcNow.AddDays(7),
        });
        _db.Subscriptions.Add(new Subscription
        {
            StudioId = studioId,
            Status = status,
            TrialExpiresAt = DateTime.UtcNow.AddDays(7),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
            StripeSubscriptionId = stripeId,
            PendingPlanId = pendingPlanId,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return studioId;
    }

    // ── Revenue ledger ────────────────────────────────────────────────────

    private async Task SetBilled(Guid studioId, decimal amount)
    {
        Subscription sub = _db.Subscriptions.Single(s => s.StudioId == studioId);
        sub.BilledUnitAmount = amount;
        sub.BilledQuantity = 1;
        sub.BilledCurrency = "eur";
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.PastDue)]
    public async Task Handle_BillingMonthlySubscription_WritesChurnToZero(SubscriptionStatus status)
    {
        Guid studioId = await SeedStudio(status, stripeId: null);
        await SetBilled(studioId, 59m);

        await CreateSut().Handle(await Confirmed(studioId), default);

        SubscriptionRevenueEvent ledgerEvent = _db.SubscriptionRevenueEvents.Single();
        ledgerEvent.Type.Should().Be(RevenueEventType.Churn);
        ledgerEvent.MrrBefore.Should().Be(59m);
        ledgerEvent.MrrAfter.Should().Be(0m);
        ledgerEvent.Source.Should().Be(nameof(CancelSubscriptionHandler));
    }

    [Theory]
    [InlineData(SubscriptionStatus.Trialing)]
    [InlineData(SubscriptionStatus.GracePeriod)]
    public async Task Handle_SubscriptionThatNeverBilled_WritesNoChurn(SubscriptionStatus status)
    {
        Guid studioId = await SeedStudio(status, stripeId: null);
        await SetBilled(studioId, 59m);

        await CreateSut().Handle(await Confirmed(studioId), default);

        _db.SubscriptionRevenueEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_YearlyCancelledWithRefund_WritesChurnOfMonthlyEquivalent()
    {
        (Guid studioId, _) = await SeedYearlyStudioWithInvoice("sub_yr_ledger", 590m, 59m, monthsAgo: 3);
        await SetBilled(studioId, 590m);

        await CreateSut().Handle(await Confirmed(studioId), default);

        SubscriptionRevenueEvent ledgerEvent = _db.SubscriptionRevenueEvents.Single();
        ledgerEvent.Type.Should().Be(RevenueEventType.Churn);
        ledgerEvent.MrrBefore.Should().BeApproximately(49.17m, 0.01m); // 590 / 12
        ledgerEvent.MrrAfter.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_CancelThenStripeDeletedEcho_WritesExactlyOneChurn()
    {
        Guid studioId = await SeedStudio(SubscriptionStatus.Active, stripeId: "sub_echo");
        await SetBilled(studioId, 59m);

        await CreateSut().Handle(await Confirmed(studioId), default);
        _db.ChangeTracker.Clear();
        await new Pena_e_Arte.Application.Billing.Commands.HandleSubscriptionDeletedHandler(_db).Handle(
            new Pena_e_Arte.Application.Billing.Commands.HandleSubscriptionDeletedCommand("sub_echo", "evt_echo"),
            default);

        _db.SubscriptionRevenueEvents.Count().Should().Be(1);
    }
}
