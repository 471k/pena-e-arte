using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Application.Platform.Commands;
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
        new(_db, _stripe, _currentUser, NullLogger<CancelSubscriptionHandler>.Instance);

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Trialing)]
    [InlineData(SubscriptionStatus.GracePeriod)]
    public async Task Handle_CancellableStatus_SetsStatusToCancelled(SubscriptionStatus status)
    {
        Guid studioId = await SeedStudio(status, stripeId: null);

        await CreateSut().Handle(new CancelSubscriptionCommand(studioId), default);

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

        await CreateSut().Handle(new CancelSubscriptionCommand(studioId), default);

        await _stripe.Received(1).CancelSubscriptionAsync(stripeId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithoutStripeSubscriptionId_DoesNotCallStripe()
    {
        Guid studioId = await SeedStudio(SubscriptionStatus.Active, stripeId: null);

        await CreateSut().Handle(new CancelSubscriptionCommand(studioId), default);

        await _stripe.DidNotReceive().CancelSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StripeCallFails_DoesNotThrow_AndLocalRecordIsStillCancelled()
    {
        const string stripeId = "sub_fail_456";
        Guid studioId = await SeedStudio(SubscriptionStatus.Active, stripeId: stripeId);

        _stripe.CancelSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .ThrowsAsync(new Exception("Stripe timeout"));

        Func<Task> act = () => CreateSut().Handle(new CancelSubscriptionCommand(studioId), default);

        await act.Should().NotThrowAsync();
        _db.Subscriptions.Single(s => s.StudioId == studioId)
            .Status.Should().Be(SubscriptionStatus.Cancelled);
    }

    [Fact]
    public async Task Handle_ClearsPendingPlanId()
    {
        Guid planId = Guid.NewGuid();
        Guid studioId = await SeedStudio(SubscriptionStatus.Active, stripeId: null, pendingPlanId: planId);

        await CreateSut().Handle(new CancelSubscriptionCommand(studioId), default);

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

        Func<Task> act = () => CreateSut().Handle(new CancelSubscriptionCommand(studioId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Theory]
    [InlineData(SubscriptionStatus.Cancelled)]
    public async Task Handle_AlreadyCancelledStatus_ThrowsBusinessRuleViolation(SubscriptionStatus status)
    {
        Guid studioId = await SeedStudio(status, stripeId: null);

        Func<Task> act = () => CreateSut().Handle(new CancelSubscriptionCommand(studioId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    // --- §2.5 admin refund override ---

    [Fact]
    public async Task Handle_YearlyWithInvoice_NoOverride_UsesFormula()
    {
        const string stripeId = "sub_yearly_formula";
        (Guid studioId, Guid subId) = await SeedYearlyStudioWithInvoice(stripeId, amountPaid: 790m, monthlyRef: 79m, monthsAgo: 3);

        await CreateSut().Handle(new CancelSubscriptionCommand(studioId), default);

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
            new CancelSubscriptionCommand(studioId, RefundRule.AdminFull, "Goodwill"), default);

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
            new CancelSubscriptionCommand(studioId, RefundRule.AdminNone, "Policy violation"), default);

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
            new CancelSubscriptionCommand(studioId, RefundRule.AdminFull, "n/a"), default);

        _db.SubscriptionRefunds.Any().Should().BeFalse();
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
            // +5 minutes — guards against the exact-anniversary boundary: without it, the
            // real time elapsed between seeding "now" and the handler's own DateTime.UtcNow
            // can tip MonthsUsed's monthly-anniversary loop into counting one extra started
            // month than intended (YearlyRefundCalculator itself is correct — see
            // YearlyRefundCalculatorTests — this is purely a test-seeding timing guard).
            PaidAt = DateTime.UtcNow.AddMonths(-monthsAgo).AddMinutes(5),
            PeriodStart = DateTime.UtcNow.AddMonths(-monthsAgo).AddMinutes(5),
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
}
