using FluentAssertions;
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

public class ActivateCheckoutSubscriptionHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IStripeBillingService _billing = Substitute.For<IStripeBillingService>();
    private readonly IReferralRewardService _rewardService = Substitute.For<IReferralRewardService>();
    private readonly Guid _studioId = Guid.NewGuid();

    private ActivateCheckoutSubscriptionHandler CreateSut() =>
        new(_db, _billing, _rewardService, NullLogger<ActivateCheckoutSubscriptionHandler>.Instance);

    private void StripeReturns(
        bool complete, string subId = "sub_new", string cust = "cus_new",
        string price = "price_growth", Guid? studioRef = null, bool hasDiscount = true)
    {
        _billing.GetCheckoutSubscriptionAsync("cs_123", Arg.Any<CancellationToken>())
            .Returns(new CheckoutSubscriptionResult(
                complete, subId, cust, (studioRef ?? _studioId).ToString(), price,
                DateTime.UtcNow.AddMonths(1), HasDiscount: hasDiscount));
    }

    [Fact]
    public async Task Handle_CompletedSession_ActivatesAndLinksSubscription()
    {
        Plan plan = await SeedPlan("price_growth");
        await SeedStudioSubscription(SubscriptionStatus.Trialing);
        StripeReturns(complete: true, price: "price_growth");

        SubscriptionResponse? result = await CreateSut()
            .Handle(new ActivateCheckoutSubscriptionCommand("cs_123", null), default);

        result.Should().NotBeNull();
        result!.Status.Should().Be(SubscriptionStatus.Active.ToString());

        Subscription stored = _db.Subscriptions.Single(s => s.StudioId == _studioId);
        stored.Status.Should().Be(SubscriptionStatus.Active);
        stored.StripeSubscriptionId.Should().Be("sub_new");
        stored.PlanId.Should().Be(plan.Id);
        _db.Studios.Single(s => s.Id == _studioId).StripeCustomerId.Should().Be("cus_new");
    }

    [Fact]
    public async Task Handle_CompletedSession_SnapshotsBilledAmountFromStripe()
    {
        await SeedPlan("price_growth");
        await SeedStudioSubscription(SubscriptionStatus.Trialing);
        StripeReturns(complete: true, price: "price_growth");
        _billing.GetPriceAsync("price_growth", Arg.Any<CancellationToken>())
            .Returns(new StripePriceInfo(true, 5900, "eur", "month", 1));

        await CreateSut().Handle(new ActivateCheckoutSubscriptionCommand("cs_123", null), default);

        Subscription stored = _db.Subscriptions.Single(s => s.StudioId == _studioId);
        stored.BilledUnitAmount.Should().Be(59m);
        stored.BilledQuantity.Should().Be(1);
        stored.BilledCurrency.Should().Be("eur");
    }

    [Fact]
    public async Task Handle_CompletedSession_NoPriceId_LeavesSnapshotNull()
    {
        await SeedStudioSubscription(SubscriptionStatus.Trialing);
        _billing.GetCheckoutSubscriptionAsync("cs_123", Arg.Any<CancellationToken>())
            .Returns(new CheckoutSubscriptionResult(
                true, "sub_new", "cus_new", _studioId.ToString(), null,
                DateTime.UtcNow.AddMonths(1), HasDiscount: false));

        await CreateSut().Handle(new ActivateCheckoutSubscriptionCommand("cs_123", null), default);

        Subscription stored = _db.Subscriptions.Single(s => s.StudioId == _studioId);
        stored.BilledUnitAmount.Should().BeNull();
        await _billing.DidNotReceive().GetPriceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SessionNotComplete_ReturnsNullNoChange()
    {
        await SeedStudioSubscription(SubscriptionStatus.Trialing);
        StripeReturns(complete: false);

        SubscriptionResponse? result = await CreateSut()
            .Handle(new ActivateCheckoutSubscriptionCommand("cs_123", null), default);

        result.Should().BeNull();
        _db.Subscriptions.Single(s => s.StudioId == _studioId).Status.Should().Be(SubscriptionStatus.Trialing);
    }

    [Fact]
    public async Task Handle_ExpectedStudioMismatch_ThrowsNotFound()
    {
        await SeedStudioSubscription(SubscriptionStatus.Trialing);
        StripeReturns(complete: true); // session's studio == _studioId

        Func<Task> act = () => CreateSut()
            .Handle(new ActivateCheckoutSubscriptionCommand("cs_123", Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_AlreadyActiveSameSubscription_IsIdempotent()
    {
        await SeedPlan("price_growth");
        await SeedStudioSubscription(SubscriptionStatus.Active, stripeSubId: "sub_new");
        StripeReturns(complete: true, subId: "sub_new");

        SubscriptionResponse? result = await CreateSut()
            .Handle(new ActivateCheckoutSubscriptionCommand("cs_123", null), default);

        result.Should().NotBeNull();
        _db.Subscriptions.Count(s => s.StudioId == _studioId).Should().Be(1);
        _db.ReferralRedemptions.Count().Should().Be(0);
    }

    [Fact]
    public async Task Handle_PendingReferral_SessionHasDiscount_RecordsDiscountAppliedTrueAndDeactivatesSingleUse()
    {
        await SeedPlan("price_growth");
        Guid codeId = Guid.NewGuid();
        _db.ReferralCodes.Add(new ReferralCode
        {
            Id = codeId,
            StudioId = Guid.NewGuid(),
            Code = "REF12345",
            IsActive = true,
            IsSingleUse = true,
        });
        await _db.SaveChangesAsync();
        await SeedStudioSubscription(SubscriptionStatus.Trialing, pendingReferralCodeId: codeId);
        StripeReturns(complete: true, hasDiscount: true);

        await CreateSut().Handle(new ActivateCheckoutSubscriptionCommand("cs_123", null), default);

        ReferralRedemption redemption = _db.ReferralRedemptions.Single(r => r.ReferralCodeId == codeId);
        redemption.DiscountApplied.Should().BeTrue();
        redemption.NewStudioId.Should().Be(_studioId);
        _db.Studios.Single(s => s.Id == _studioId).PendingReferralCodeId.Should().BeNull();
        _db.ReferralCodes.Single(r => r.Id == codeId).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_PendingReferral_SessionHasNoDiscount_RecordsDiscountAppliedFalseAndKeepsCodeActive()
    {
        await SeedPlan("price_growth");
        Guid codeId = Guid.NewGuid();
        _db.ReferralCodes.Add(new ReferralCode
        {
            Id = codeId,
            StudioId = Guid.NewGuid(),
            Code = "REF99999",
            IsActive = true,
            IsSingleUse = true,
        });
        await _db.SaveChangesAsync();
        await SeedStudioSubscription(SubscriptionStatus.Trialing, pendingReferralCodeId: codeId);
        StripeReturns(complete: true, hasDiscount: false);

        await CreateSut().Handle(new ActivateCheckoutSubscriptionCommand("cs_123", null), default);

        ReferralRedemption redemption = _db.ReferralRedemptions.Single(r => r.ReferralCodeId == codeId);
        redemption.DiscountApplied.Should().BeFalse(
            "coupon creation failed so no discount was attached to the session");
        _db.ReferralCodes.Single(r => r.Id == codeId).IsActive.Should().BeTrue(
            "single-use code should not be consumed if no discount was applied");
        _db.Studios.Single(s => s.Id == _studioId).PendingReferralCodeId.Should().BeNull();
    }

    private async Task<Plan> SeedPlan(string priceMonthly)
    {
        Plan plan = new() { Name = "Growth" };
        plan.Prices.Add(new PlanPrice
        {
            Interval = BillingInterval.Monthly,
            Price = 59m,
            StripePriceId = priceMonthly,
        });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        return plan;
    }

    private async Task SeedStudioSubscription(
        SubscriptionStatus status, string? stripeSubId = null, Guid? pendingReferralCodeId = null)
    {
        _db.Studios.Add(new Studio
        {
            Id = _studioId,
            Name = "Studio",
            Slug = "studio",
            OwnerEmail = "owner@test.com",
            PendingReferralCodeId = pendingReferralCodeId,
        });
        _db.Subscriptions.Add(new Subscription
        {
            StudioId = _studioId,
            Status = status,
            StripeSubscriptionId = stripeSubId,
            TrialExpiresAt = DateTime.UtcNow.AddDays(7),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(7),
            GracePeriodEnd = DateTime.UtcNow.AddDays(14),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    // ── Revenue ledger ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_FirstActivation_WritesNewLedgerEvent()
    {
        await SeedPlan("price_growth");
        await SeedStudioSubscription(SubscriptionStatus.Trialing);
        StripeReturns(complete: true, price: "price_growth");
        _billing.GetPriceAsync("price_growth", Arg.Any<CancellationToken>())
            .Returns(new StripePriceInfo(true, 5900, "eur", "month", 1));

        await CreateSut().Handle(new ActivateCheckoutSubscriptionCommand("cs_123", null), default);

        SubscriptionRevenueEvent ledgerEvent = _db.SubscriptionRevenueEvents.Single();
        ledgerEvent.Type.Should().Be(RevenueEventType.New);
        ledgerEvent.SubscriptionId.Should().Be(_db.Subscriptions.Single(s => s.StudioId == _studioId).Id);
        ledgerEvent.MrrBefore.Should().Be(0m);
        ledgerEvent.MrrAfter.Should().Be(59m);
        ledgerEvent.Source.Should().Be(nameof(ActivateCheckoutSubscriptionHandler));
    }

    [Fact]
    public async Task Handle_StudioThatChurnedBefore_WritesReactivationNotNew()
    {
        await SeedPlan("price_growth");
        await SeedStudioSubscription(SubscriptionStatus.Cancelled);
        _db.SubscriptionRevenueEvents.Add(new SubscriptionRevenueEvent
        {
            SubscriptionId = _db.Subscriptions.Single(s => s.StudioId == _studioId).Id,
            StudioId = _studioId,
            OccurredAt = DateTime.UtcNow.AddMonths(-2),
            Type = RevenueEventType.Churn,
            MrrBefore = 59m,
            MrrAfter = 0m,
            Source = "seed",
            StripeEventId = "seed-churn",
        });
        await _db.SaveChangesAsync();
        StripeReturns(complete: true, price: "price_growth");
        _billing.GetPriceAsync("price_growth", Arg.Any<CancellationToken>())
            .Returns(new StripePriceInfo(true, 5900, "eur", "month", 1));

        await CreateSut().Handle(new ActivateCheckoutSubscriptionCommand("cs_123", null), default);

        SubscriptionRevenueEvent added = _db.SubscriptionRevenueEvents.Single(e => e.Source != "seed");
        added.Type.Should().Be(RevenueEventType.Reactivation);
        added.MrrBefore.Should().Be(0m);
        added.MrrAfter.Should().Be(59m);
    }

    [Fact]
    public async Task Handle_AlreadyLinkedAndActive_WritesNoLedgerEvent()
    {
        await SeedPlan("price_growth");
        await SeedStudioSubscription(SubscriptionStatus.Active, stripeSubId: "sub_new");
        StripeReturns(complete: true, subId: "sub_new", price: "price_growth");

        await CreateSut().Handle(new ActivateCheckoutSubscriptionCommand("cs_123", null), default);

        _db.SubscriptionRevenueEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NoBilledAmountResolvable_WritesNoLedgerEvent()
    {
        await SeedStudioSubscription(SubscriptionStatus.Trialing);
        _billing.GetCheckoutSubscriptionAsync("cs_123", Arg.Any<CancellationToken>())
            .Returns(new CheckoutSubscriptionResult(
                true, "sub_new", "cus_new", _studioId.ToString(), null,
                DateTime.UtcNow.AddMonths(1), HasDiscount: false));

        await CreateSut().Handle(new ActivateCheckoutSubscriptionCommand("cs_123", null), default);

        _db.SubscriptionRevenueEvents.Should().BeEmpty();
    }
}
