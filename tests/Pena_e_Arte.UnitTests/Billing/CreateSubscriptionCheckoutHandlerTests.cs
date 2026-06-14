using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class CreateSubscriptionCheckoutHandlerTests
{
    private readonly FakeDbContext          _db        = FakeDbContext.Create();
    private readonly ICurrentTenant         _tenant    = Substitute.For<ICurrentTenant>();
    private readonly IStripeBillingService  _billing   = Substitute.For<IStripeBillingService>();
    private readonly IStripeDiscountService _discounts = Substitute.For<IStripeDiscountService>();
    private readonly Guid                   _studioId  = Guid.NewGuid();

    public CreateSubscriptionCheckoutHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _billing.CreateSubscriptionCheckoutAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns("https://checkout.stripe.com/c/pay/cs_test_123");
        _billing.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("cus_new");
    }

    private CreateSubscriptionCheckoutHandler CreateSut() =>
        new(_db, _tenant, _billing, _discounts, NullLogger<CreateSubscriptionCheckoutHandler>.Instance);

    private static CreateCheckoutRequest Req(Guid planId) =>
        new(planId, "https://app.test/billing?session_id={CHECKOUT_SESSION_ID}", "https://app.test/billing/subscribe");

    [Fact]
    public async Task Handle_ValidPlan_ReturnsCheckoutUrlWithCorrectPrice()
    {
        Plan plan = await SeedPlan("price_growth");
        await SeedStudioSubscription(SubscriptionStatus.Trialing, stripeCustomerId: "cus_existing");

        CheckoutSessionResponse result = await CreateSut()
            .Handle(new CreateSubscriptionCheckoutCommand(Req(plan.Id)), default);

        result.Url.Should().StartWith("https://checkout.stripe.com");
        await _billing.Received(1).CreateSubscriptionCheckoutAsync(
            Arg.Is("cus_existing"), Arg.Is("price_growth"), Arg.Is(_studioId.ToString()),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Is<string?>(c => c == null),
            Arg.Is<DateTime?>(t => t == null), Arg.Any<CancellationToken>()); // Trialing → no cash credit
    }

    [Fact]
    public async Task Handle_NoStripeCustomer_CreatesOneFirst()
    {
        Plan plan = await SeedPlan("price_growth");
        await SeedStudioSubscription(SubscriptionStatus.Trialing, stripeCustomerId: null);

        await CreateSut().Handle(new CreateSubscriptionCheckoutCommand(Req(plan.Id)), default);

        await _billing.Received(1).CreateCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.Studios.Single(s => s.Id == _studioId).StripeCustomerId.Should().Be("cus_new");
    }

    [Fact]
    public async Task Handle_AlreadyCardBilled_ThrowsBusinessRuleViolation()
    {
        Plan plan = await SeedPlan("price_growth");
        await SeedStudioSubscription(SubscriptionStatus.Active, stripeCustomerId: "cus_x", stripeSubId: "sub_existing");

        Func<Task> act = () => CreateSut().Handle(new CreateSubscriptionCheckoutCommand(Req(plan.Id)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>().WithMessage("*already*card*");
    }

    [Fact]
    public async Task Handle_CashBilledActive_AllowsCheckoutAndCreditsCashPeriodAsTrial()
    {
        // Active but cash-billed (no Stripe subscription) → owner may set up card billing,
        // and the first card charge is deferred to the cash period end (trial_end).
        Plan plan = await SeedPlan("price_growth");
        DateTime cashEnd = DateTime.UtcNow.AddDays(30);
        await SeedStudioSubscription(
            SubscriptionStatus.Active, stripeCustomerId: "cus_x", stripeSubId: null, currentPeriodEnd: cashEnd);

        CheckoutSessionResponse result = await CreateSut()
            .Handle(new CreateSubscriptionCheckoutCommand(Req(plan.Id)), default);

        result.Url.Should().StartWith("https://checkout.stripe.com");
        await _billing.Received(1).CreateSubscriptionCheckoutAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Is<DateTime?>(t => t.HasValue && t.Value == cashEnd), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PlanWithoutStripePrice_ThrowsBusinessRuleViolation()
    {
        Plan plan = await SeedPlan(stripePriceMonthly: null);
        await SeedStudioSubscription(SubscriptionStatus.Trialing, stripeCustomerId: "cus_x");

        Func<Task> act = () => CreateSut().Handle(new CreateSubscriptionCheckoutCommand(Req(plan.Id)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*not available for online checkout*");
    }

    [Fact]
    public async Task Handle_PendingValidReferral_AttachesCouponToCheckout()
    {
        Plan plan   = await SeedPlan("price_growth");
        Guid codeId = Guid.NewGuid();
        _db.ReferralCodes.Add(new ReferralCode { Id = codeId, StudioId = Guid.NewGuid(), Code = "REF12345", IsActive = true });
        await _db.SaveChangesAsync();
        await SeedStudioSubscription(SubscriptionStatus.Trialing, stripeCustomerId: "cus_x", pendingReferralCodeId: codeId);
        _discounts.CreateOneMonthFreeCouponAsync(Arg.Any<CancellationToken>()).Returns("coupon_abc");

        await CreateSut().Handle(new CreateSubscriptionCheckoutCommand(Req(plan.Id)), default);

        await _billing.Received(1).CreateSubscriptionCheckoutAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Is<string?>("coupon_abc"),
            Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    private async Task<Plan> SeedPlan(string? stripePriceMonthly = "price_monthly")
    {
        Plan plan = new()
        {
            Name                 = "Growth",
            BillingInterval      = BillingInterval.Monthly,
            PriceMonthly         = 59m,
            PriceYearly          = 590m,
            StripePriceIdMonthly = stripePriceMonthly,
        };
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        return plan;
    }

    private async Task SeedStudioSubscription(
        SubscriptionStatus status, string? stripeCustomerId,
        Guid? pendingReferralCodeId = null, string? stripeSubId = null, DateTime? currentPeriodEnd = null)
    {
        _db.Studios.Add(new Studio
        {
            Id                    = _studioId,
            Name                  = "Studio",
            Slug                  = "studio",
            OwnerEmail            = "owner@test.com",
            StripeCustomerId      = stripeCustomerId,
            PendingReferralCodeId = pendingReferralCodeId,
        });
        _db.Subscriptions.Add(new Subscription
        {
            StudioId             = _studioId,
            Status               = status,
            StripeSubscriptionId = stripeSubId,
            TrialExpiresAt       = DateTime.UtcNow.AddDays(7),
            CurrentPeriodEnd     = currentPeriodEnd ?? DateTime.UtcNow.AddDays(7),
            GracePeriodEnd       = DateTime.UtcNow.AddDays(14),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
