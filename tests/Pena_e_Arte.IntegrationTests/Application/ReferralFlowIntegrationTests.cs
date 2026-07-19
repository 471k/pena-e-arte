using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Application.Referrals.Commands;
using Pena_e_Arte.Application.Referrals.Queries;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class ReferralFlowIntegrationTests(DatabaseFixture fixture)
{
    // ── GenerateReferralCode ────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateReferralCode_NewStudio_PersistsCodeAndReturnsShareUrl()
    {
        Guid studioId = await SeedReferringStudio();

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GenerateReferralCodeHandler handler = new(db, MakeTenant(studioId), NullLogger<GenerateReferralCodeHandler>.Instance);

        ReferralCodeResponse result =
            await handler.Handle(new GenerateReferralCodeCommand(studioId), default);

        result.Code.Should().HaveLength(8);
        result.IsActive.Should().BeTrue();
        result.ShareUrl.Should().Be($"https://penaearte.com/register?ref={result.Code}");

        ReferralCode? stored = await db.ReferralCodes
            .FirstOrDefaultAsync(r => r.Id == result.Id);
        stored.Should().NotBeNull();
        stored!.Code.Should().Be(result.Code);
    }

    // ── GetReferralCode ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetReferralCode_AfterGenerate_ReturnsActiveCode()
    {
        Guid studioId = await SeedReferringStudio();
        string code   = await GenerateCode(studioId);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GetReferralCodeHandler handler = new(db, MakeTenant(studioId));

        ReferralCodeResponse? result =
            await handler.Handle(new GetReferralCodeQuery(studioId), default);

        result.Should().NotBeNull();
        result!.Code.Should().Be(code);
    }

    [Fact]
    public async Task GetReferralCode_NoCodeGenerated_ReturnsNull()
    {
        Guid studioId = await SeedReferringStudio();

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GetReferralCodeHandler handler = new(db, MakeTenant(studioId));

        ReferralCodeResponse? result =
            await handler.Handle(new GetReferralCodeQuery(studioId), default);

        result.Should().BeNull();
    }

    // ── Full referral flow ──────────────────────────────────────────────────────

    [Fact]
    public async Task FullReferralFlow_RegisterWithCode_ThenSubscribe_CreatesRedemptionWithDiscount()
    {
        // 1. Referring studio generates a code
        Guid referringStudioId = await SeedReferringStudio();
        string code = await GenerateCode(referringStudioId);

        // 2. New studio registers using the referral code
        Guid planId = await SeedPlan();

        RegisterStudioHandler registerHandler = new(
            fixture.CreateDbContext(Guid.Empty),
            Substitute.For<IJobScheduler>(),
            NullLogger<RegisterStudioHandler>.Instance);

        string newSlug = ("ref-" + Guid.NewGuid().ToString("N"))[..20];
        StudioResponse newStudio = await registerHandler.Handle(
            new RegisterStudioCommand(new RegisterStudioRequest(
                Name:         "New Referred Studio",
                Slug:         newSlug,
                City:         "Lisboa",
                Latitude:     38.7,
                Longitude:    -9.1,
                OwnerEmail:   $"{newSlug}@test.com",
                ReferralCode: code)),
            default);

        // Verify PendingReferralCodeId was stored
        await using AppDbContext verifyDb = fixture.CreateDbContext(Guid.Empty);
        Studio? stored = await verifyDb.Studios.FirstOrDefaultAsync(s => s.Id == newStudio.Id);
        stored!.PendingReferralCodeId.Should().NotBeNull();

        // 3. New studio subscribes — discount should be applied
        IStripeBillingService billing = Substitute.For<IStripeBillingService>();
        billing.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns("cus_flow_test");
        billing.CreateSubscriptionAsync(
                   Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
               .Returns(("sub_flow_test", DateTime.UtcNow.AddMonths(1)));

        IStripeDiscountService discounts = Substitute.For<IStripeDiscountService>();
        discounts.CreateOneMonthFreeCouponAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns("coup_flow_test");

        CurrentTenantService tenantSvc = new();
        tenantSvc.SetTenant(newStudio.Id);

        ReferralRewardService rewardSvc = new(
            fixture.CreateDbContext(Guid.Empty), billing, discounts,
            NullLogger<ReferralRewardService>.Instance);

        CreateSubscriptionHandler subHandler = new(
            fixture.CreateDbContext(Guid.Empty),
            tenantSvc,
            billing,
            discounts,
            rewardSvc,
            NullLogger<CreateSubscriptionHandler>.Instance);

        await subHandler.Handle(
            new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId)), default);

        // 4. Verify ReferralRedemption was created with DiscountApplied = true
        await using AppDbContext finalDb = fixture.CreateDbContext(Guid.Empty);

        ReferralCode? refCode = await finalDb.ReferralCodes
            .FirstOrDefaultAsync(r => r.Code == code);
        refCode.Should().NotBeNull();

        ReferralRedemption? redemption = await finalDb.ReferralRedemptions
            .FirstOrDefaultAsync(r => r.ReferralCodeId == refCode!.Id);
        redemption.Should().NotBeNull();
        redemption!.NewStudioId.Should().Be(newStudio.Id);
        redemption.DiscountApplied.Should().BeTrue();

        // Single-use code should now be inactive
        refCode!.IsActive.Should().BeFalse();

        // PendingReferralCodeId cleared on studio
        Studio? finalStudio = await finalDb.Studios.FirstOrDefaultAsync(s => s.Id == newStudio.Id);
        finalStudio!.PendingReferralCodeId.Should().BeNull();
    }

    [Fact]
    public async Task RegisterWithInvalidCode_ThrowsBusinessRuleViolationException()
    {
        RegisterStudioHandler handler = new(
            fixture.CreateDbContext(Guid.Empty),
            Substitute.For<IJobScheduler>(),
            NullLogger<RegisterStudioHandler>.Instance);

        string newSlug = ("bad-" + Guid.NewGuid().ToString("N"))[..20];
        Func<Task> act = () => handler.Handle(
            new RegisterStudioCommand(new RegisterStudioRequest(
                Name:         "Bad Code Studio",
                Slug:         newSlug,
                City:         "Porto",
                Latitude:     41.1,
                Longitude:    -8.6,
                OwnerEmail:   $"{newSlug}@test.com",
                ReferralCode: "BADCODE1")),
            default);

        await act.Should().ThrowAsync<Pena_e_Arte.Domain.Exceptions.BusinessRuleViolationException>()
            .WithMessage("*invalid*");
    }

    [Fact]
    public async Task FullReferralFlow_WithReferrerOnStripeSub_AppliesRewardCoupon()
    {
        // 1. Referring studio is seeded with an active Stripe subscription.
        Guid referringStudioId = await SeedReferringStudio();
        string referrerStripeSubId = await SeedReferrerSubscription(referringStudioId);
        string code = await GenerateCode(referringStudioId);

        // 2. New studio registers with the referral code.
        Guid planId   = await SeedPlan();
        Guid newStudioId = await RegisterNewStudio(code);

        // 3. Mock Stripe services.
        IStripeBillingService billing = Substitute.For<IStripeBillingService>();
        billing.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns("cus_two_sided");
        billing.CreateSubscriptionAsync(
                   Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
               .Returns(("sub_two_sided", DateTime.UtcNow.AddMonths(1)));

        IStripeDiscountService discounts = Substitute.For<IStripeDiscountService>();
        discounts.CreateOneMonthFreeCouponAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns("coup_two_sided");

        ReferralRewardService rewardSvc = new(
            fixture.CreateDbContext(Guid.Empty), billing, discounts,
            NullLogger<ReferralRewardService>.Instance);

        CurrentTenantService tenantSvc = new();
        tenantSvc.SetTenant(newStudioId);

        CreateSubscriptionHandler subHandler = new(
            fixture.CreateDbContext(Guid.Empty), tenantSvc, billing, discounts,
            rewardSvc, NullLogger<CreateSubscriptionHandler>.Instance);

        // 4. New studio subscribes.
        await subHandler.Handle(
            new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId)), default);

        // 5. Verify ReferralRedemption has ReferrerRewardApplied = true.
        await using AppDbContext verifyDb = fixture.CreateDbContext(Guid.Empty);
        ReferralRedemption? redemption = await verifyDb.ReferralRedemptions
            .FirstOrDefaultAsync(r => r.NewStudioId == newStudioId);
        redemption.Should().NotBeNull();
        redemption!.ReferrerRewardApplied.Should().BeTrue();
        redemption.ReferrerRewardCouponId.Should().Be("coup_two_sided");

        // 6. Verify ApplyCouponToActiveSubscriptionAsync was called with the referrer's sub ID.
        await billing.Received(1).ApplyCouponToActiveSubscriptionAsync(
            referrerStripeSubId, "coup_two_sided", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FullReferralFlow_ReferrerNotOnStripeSub_RedemptionSaved_RewardNotApplied()
    {
        // Referring studio has no Stripe subscription → reward not applied, but
        // redemption is still recorded and the new studio's discount is unaffected.
        Guid referringStudioId = await SeedReferringStudio();
        string code = await GenerateCode(referringStudioId);

        Guid planId      = await SeedPlan();
        Guid newStudioId = await RegisterNewStudio(code);

        IStripeBillingService billing = Substitute.For<IStripeBillingService>();
        billing.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns("cus_no_reward");
        billing.CreateSubscriptionAsync(
                   Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
               .Returns(("sub_no_reward", DateTime.UtcNow.AddMonths(1)));

        IStripeDiscountService discounts = Substitute.For<IStripeDiscountService>();
        discounts.CreateOneMonthFreeCouponAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns("coup_no_reward");

        ReferralRewardService rewardSvc = new(
            fixture.CreateDbContext(Guid.Empty), billing, discounts,
            NullLogger<ReferralRewardService>.Instance);

        CurrentTenantService tenantSvc = new();
        tenantSvc.SetTenant(newStudioId);

        await new CreateSubscriptionHandler(
            fixture.CreateDbContext(Guid.Empty), tenantSvc, billing, discounts,
            rewardSvc, NullLogger<CreateSubscriptionHandler>.Instance)
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId)), default);

        await using AppDbContext verifyDb = fixture.CreateDbContext(Guid.Empty);
        ReferralRedemption? redemption = await verifyDb.ReferralRedemptions
            .FirstOrDefaultAsync(r => r.NewStudioId == newStudioId);

        redemption!.DiscountApplied.Should().BeTrue();
        redemption.ReferrerRewardApplied.Should().BeFalse();

        await billing.DidNotReceive().ApplyCouponToActiveSubscriptionAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<Guid> SeedReferringStudio()
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        Studio studio = new()
        {
            Name           = "Referring Studio",
            Slug           = ("ref-src-" + Guid.NewGuid().ToString("N"))[..20],
            City           = "Porto",
            OwnerEmail     = $"ref{Guid.NewGuid():N}@test.com",
            IsActive       = true,
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
        };
        db.Studios.Add(studio);
        await db.SaveChangesAsync();
        return studio.Id;
    }

    private async Task<string> GenerateCode(Guid studioId)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GenerateReferralCodeHandler handler = new(db, MakeTenant(studioId), NullLogger<GenerateReferralCodeHandler>.Instance);
        ReferralCodeResponse result = await handler.Handle(new GenerateReferralCodeCommand(studioId), default);
        return result.Code;
    }

    private static ICurrentTenant MakeTenant(Guid studioId)
    {
        ICurrentTenant tenant = Substitute.For<ICurrentTenant>();
        tenant.StudioId.Returns(studioId);
        return tenant;
    }

    private async Task<Guid> SeedPlan()
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        Plan plan = new() { Name = "Pro", BillingInterval = BillingInterval.Monthly, PriceMonthly = 49m };
        db.Plans.Add(plan);
        await db.SaveChangesAsync();
        return plan.Id;
    }

    private async Task<string> SeedReferrerSubscription(Guid studioId)
    {
        string stripeSubId = $"sub_referrer_{studioId:N}";
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);

        Plan plan = new() { Name = "Referrer Plan", BillingInterval = BillingInterval.Monthly, PriceMonthly = 49m };
        db.Plans.Add(plan);

        Subscription? existing = await db.Subscriptions.FirstOrDefaultAsync(s => s.StudioId == studioId);
        if (existing is not null)
        {
            existing.PlanId               = plan.Id;
            existing.Status               = SubscriptionStatus.Active;
            existing.StripeSubscriptionId = stripeSubId;
            existing.CurrentPeriodEnd     = DateTime.UtcNow.AddMonths(1);
        }
        else
        {
            db.Subscriptions.Add(new Subscription
            {
                StudioId             = studioId,
                PlanId               = plan.Id,
                Status               = SubscriptionStatus.Active,
                StripeSubscriptionId = stripeSubId,
                CurrentPeriodEnd     = DateTime.UtcNow.AddMonths(1),
            });
        }
        await db.SaveChangesAsync();
        return stripeSubId;
    }

    private async Task<Guid> RegisterNewStudio(string referralCode)
    {
        RegisterStudioHandler handler = new(
            fixture.CreateDbContext(Guid.Empty),
            Substitute.For<IJobScheduler>(),
            NullLogger<RegisterStudioHandler>.Instance);

        string slug = ("rwd-" + Guid.NewGuid().ToString("N"))[..20];
        StudioResponse studio = await handler.Handle(
            new RegisterStudioCommand(new RegisterStudioRequest(
                Name: "Reward Test Studio", Slug: slug, City: "Lisbon",
                Latitude: 38.7, Longitude: -9.1,
                OwnerEmail: $"{slug}@test.com", ReferralCode: referralCode)),
            default);

        return studio.Id;
    }
}
