using FluentAssertions;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.UnitTests.Platform;

public class MrrRulesTests
{
    private static readonly DateTime Now = new(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);

    private static Plan PremiumPlan() =>
        new()
        {
            Name = "Premium",
            Prices =
            {
                new PlanPrice { Interval = BillingInterval.Monthly, Price = 79m },
                new PlanPrice { Interval = BillingInterval.Yearly, Price = 790m },
            },
        };

    private static DateTime EndOfMonth(int year, int month) =>
        MrrRules.EndOfMonth(new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void CreatedJan1_TrialEndsJan15_ActiveMonthly()
    {
        Plan plan = PremiumPlan();
        Subscription sub = new()
        {
            PlanId = plan.Id,
            Plan = plan,
            BillingInterval = BillingInterval.Monthly,
            Status = SubscriptionStatus.Active,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CurrentPeriodEnd = Now.AddDays(30),
        };
        SubscriptionRevenueInput input = new(sub, new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), null, true, null);

        MrrRules.MrrAt([input], EndOfMonth(2025, 12), Now).Should().Be(0m);
        MrrRules.MrrAt([input], EndOfMonth(2026, 1), Now).Should().Be(79m);
        MrrRules.MrrAt([input], Now, Now).Should().Be(79m);
    }

    [Fact]
    public void CreatedJan1_TrialEndsJan15_ActiveYearly_EveryCountedPointIsPrecise()
    {
        Plan plan = PremiumPlan();
        Subscription sub = new()
        {
            PlanId = plan.Id,
            Plan = plan,
            BillingInterval = BillingInterval.Yearly,
            Status = SubscriptionStatus.Active,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CurrentPeriodEnd = Now.AddDays(30),
        };
        SubscriptionRevenueInput input = new(sub, new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), null, true, null);

        decimal expected = 790m / 12m;
        MrrRules.MrrAt([input], EndOfMonth(2026, 1), Now).Should().Be(expected);
        MrrRules.MrrAt([input], Now, Now).Should().Be(expected);
    }

    [Fact]
    public void PaidOnTrialDay5_StartClampsToNow()
    {
        Plan plan = PremiumPlan();
        Subscription sub = new()
        {
            PlanId = plan.Id,
            Plan = plan,
            BillingInterval = BillingInterval.Monthly,
            Status = SubscriptionStatus.Active,
            CreatedAt = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc),
            CurrentPeriodEnd = Now.AddDays(30),
        };
        SubscriptionRevenueInput input = new(sub, new DateTime(2026, 6, 24, 0, 0, 0, DateTimeKind.Utc), null, true, null);

        MrrRules.MrrAt([input], Now, Now).Should().Be(79m);
        MrrRules.MrrAt([input], EndOfMonth(2026, 5), Now).Should().Be(0m);
    }

    [Fact]
    public void AdminCancelled_UsesAuditTimestampNotCurrentPeriodEnd()
    {
        Plan plan = PremiumPlan();
        Subscription sub = new()
        {
            PlanId = plan.Id,
            Plan = plan,
            BillingInterval = BillingInterval.Monthly,
            Status = SubscriptionStatus.Cancelled,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CurrentPeriodEnd = new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc),
        };
        DateTime adminCancelledAt = new(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        SubscriptionRevenueInput input = new(sub, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), adminCancelledAt, true, null);

        MrrRules.MrrAt([input], EndOfMonth(2026, 2), Now).Should().Be(79m);
        MrrRules.MrrAt([input], EndOfMonth(2026, 3), Now).Should().Be(0m);
        MrrRules.MrrAt([input], Now, Now).Should().Be(0m);
    }

    [Fact]
    public void StripeDeletedNoAuditRow_FallsBackToCurrentPeriodEnd()
    {
        Plan plan = PremiumPlan();
        Subscription sub = new()
        {
            PlanId = plan.Id,
            Plan = plan,
            BillingInterval = BillingInterval.Monthly,
            Status = SubscriptionStatus.Cancelled,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CurrentPeriodEnd = new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc),
        };
        SubscriptionRevenueInput input = new(sub, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, true, null);

        MrrRules.MrrAt([input], EndOfMonth(2026, 3), Now).Should().Be(79m);
        MrrRules.MrrAt([input], EndOfMonth(2026, 4), Now).Should().Be(0m);
    }

    [Fact]
    public void PastDueNow_PayingSinceJan_CountsInHistoryNotNow_AtRisk()
    {
        Plan plan = PremiumPlan();
        Subscription sub = new()
        {
            PlanId = plan.Id,
            Plan = plan,
            BillingInterval = BillingInterval.Monthly,
            Status = SubscriptionStatus.PastDue,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CurrentPeriodEnd = Now.AddDays(-5),
        };
        SubscriptionRevenueInput input = new(sub, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, true, null);

        MrrRules.MrrAt([input], EndOfMonth(2026, 5), Now).Should().Be(79m);
        MrrRules.MrrAt([input], Now, Now).Should().Be(0m);
        MrrRules.AtRiskMrr([input]).Should().Be(79m);
    }

    [Fact]
    public void ActiveCancelAtPeriodEnd_StaysInMrr_AlsoScheduledChurn()
    {
        Plan plan = PremiumPlan();
        Subscription sub = new()
        {
            PlanId = plan.Id,
            Plan = plan,
            BillingInterval = BillingInterval.Monthly,
            Status = SubscriptionStatus.Active,
            CancelAtPeriodEnd = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CurrentPeriodEnd = Now.AddDays(10),
        };
        SubscriptionRevenueInput input = new(sub, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, true, null);

        MrrRules.MrrAt([input], Now, Now).Should().Be(79m);
        MrrRules.ScheduledChurnMrr([input]).Should().Be(79m);
    }

    [Fact]
    public void TrialingWithNullPlan_IsZeroEverywhere()
    {
        Subscription sub = new()
        {
            PlanId = null,
            BillingInterval = BillingInterval.Monthly,
            Status = SubscriptionStatus.Trialing,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CurrentPeriodEnd = Now.AddDays(10),
        };
        SubscriptionRevenueInput input = new(sub, new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), null, true, null);

        MrrRules.MrrAt([input], EndOfMonth(2026, 1), Now).Should().Be(0m);
        MrrRules.MrrAt([input], Now, Now).Should().Be(0m);
        MrrRules.AtRiskMrr([input]).Should().Be(0m);
        MrrRules.PayingStudios([input]).Should().Be(0);
    }

    [Fact]
    public void ActiveOnFreePlan_ZeroMrr_ExcludedFromPayingStudios()
    {
        Plan free = new() { Name = "Free", Prices = { new PlanPrice { Interval = BillingInterval.Monthly, Price = 0m } } };
        Subscription sub = new()
        {
            PlanId = free.Id,
            Plan = free,
            BillingInterval = BillingInterval.Monthly,
            Status = SubscriptionStatus.Active,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CurrentPeriodEnd = Now.AddDays(30),
        };
        SubscriptionRevenueInput input = new(sub, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, true, null);

        MrrRules.MrrAt([input], Now, Now).Should().Be(0m);
        MrrRules.PayingStudios([input]).Should().Be(0);
    }

    [Fact]
    public void SuspendedMay12_StillSuspended_CapsWindowAndReportsPaused()
    {
        Plan plan = PremiumPlan();
        Subscription sub = new()
        {
            PlanId = plan.Id,
            Plan = plan,
            BillingInterval = BillingInterval.Monthly,
            Status = SubscriptionStatus.Active,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CurrentPeriodEnd = Now.AddDays(10),
        };
        DateTime suspendedAt = new(2026, 5, 12, 0, 0, 0, DateTimeKind.Utc);
        SubscriptionRevenueInput input = new(sub, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, false, suspendedAt);

        MrrRules.MrrAt([input], EndOfMonth(2026, 4), Now).Should().Be(79m);
        MrrRules.MrrAt([input], EndOfMonth(2026, 5), Now).Should().Be(0m);
        MrrRules.MrrAt([input], Now, Now).Should().Be(0m);
        MrrRules.PausedMrr([input]).Should().Be(79m);
    }

    [Fact]
    public void SuspendedThenUnsuspended_CountsAgainNow_NoLongerPaused()
    {
        Plan plan = PremiumPlan();
        Subscription sub = new()
        {
            PlanId = plan.Id,
            Plan = plan,
            BillingInterval = BillingInterval.Monthly,
            Status = SubscriptionStatus.Active,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CurrentPeriodEnd = Now.AddDays(10),
        };
        SubscriptionRevenueInput input = new(sub, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, true, null);

        MrrRules.MrrAt([input], Now, Now).Should().Be(79m);
        MrrRules.PausedMrr([input]).Should().Be(0m);
    }
}
