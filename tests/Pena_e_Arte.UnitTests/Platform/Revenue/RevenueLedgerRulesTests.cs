using FluentAssertions;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.UnitTests.Platform.Revenue;

public class RevenueLedgerRulesTests
{
    private static readonly Guid SubA = Guid.NewGuid();
    private static readonly Guid SubB = Guid.NewGuid();

    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 12, 0, 0, DateTimeKind.Utc);

    private static SubscriptionRevenueEvent Event(
        Guid subscriptionId, RevenueEventType type, DateTime at, decimal before, decimal after) =>
        new()
        {
            SubscriptionId = subscriptionId,
            StudioId = Guid.NewGuid(),
            OccurredAt = at,
            Type = type,
            MrrBefore = before,
            MrrAfter = after,
            Source = "test",
            StripeEventId = Guid.NewGuid().ToString(),
        };

    [Fact]
    public void MrrAt_LatestEventIsChurn_ContributesNothing()
    {
        List<SubscriptionRevenueEvent> events =
        [
            Event(SubA, RevenueEventType.New, Utc(2026, 2, 1), 0m, 49m),
            Event(SubA, RevenueEventType.Churn, Utc(2026, 3, 11), 49m, 0m),
        ];

        RevenueLedgerRules.MrrAt(events, Utc(2026, 3, 31)).Should().Be(0m);
        RevenueLedgerRules.MrrAt(events, Utc(2026, 3, 1)).Should().Be(49m); // before the churn
    }

    [Fact]
    public void MrrAt_LatestEventIsPaused_ExcludedEvenThoughMrrAfterIsPositive()
    {
        List<SubscriptionRevenueEvent> events =
        [
            Event(SubA, RevenueEventType.New, Utc(2026, 2, 1), 0m, 59m),
            Event(SubA, RevenueEventType.Paused, Utc(2026, 5, 12), 59m, 59m),
        ];

        RevenueLedgerRules.MrrAt(events, Utc(2026, 5, 31)).Should().Be(0m);
    }

    [Fact]
    public void MrrAt_LatestEventIsResumed_IncludedUsingMrrAfter()
    {
        List<SubscriptionRevenueEvent> events =
        [
            Event(SubA, RevenueEventType.New, Utc(2026, 2, 1), 0m, 59m),
            Event(SubA, RevenueEventType.Paused, Utc(2026, 5, 12), 59m, 59m),
            Event(SubA, RevenueEventType.Resumed, Utc(2026, 6, 3), 59m, 59m),
        ];

        RevenueLedgerRules.MrrAt(events, Utc(2026, 5, 31)).Should().Be(0m);
        RevenueLedgerRules.MrrAt(events, Utc(2026, 6, 30)).Should().Be(59m);
    }

    [Fact]
    public void MrrAt_PastDueExcludedRecoveredIncluded()
    {
        List<SubscriptionRevenueEvent> events =
        [
            Event(SubA, RevenueEventType.New, Utc(2026, 2, 1), 0m, 59m),
            Event(SubA, RevenueEventType.PastDue, Utc(2026, 7, 5), 59m, 59m),
            Event(SubA, RevenueEventType.Recovered, Utc(2026, 7, 9), 59m, 59m),
        ];

        RevenueLedgerRules.MrrAt(events, Utc(2026, 7, 7)).Should().Be(0m);   // at-risk in between
        RevenueLedgerRules.MrrAt(events, Utc(2026, 7, 31)).Should().Be(59m);
    }

    [Fact]
    public void MrrAt_SameOccurredAt_TiebrokenByInsertionOrderNotId()
    {
        DateTime at = Utc(2026, 3, 1);
        SubscriptionRevenueEvent first = Event(SubA, RevenueEventType.New, at, 0m, 49m);
        SubscriptionRevenueEvent second = Event(SubA, RevenueEventType.Expansion, at, 49m, 79m);
        // CreatedAt is init-only, so build the later one with an explicit later timestamp.
        SubscriptionRevenueEvent later = new()
        {
            SubscriptionId = second.SubscriptionId,
            StudioId = second.StudioId,
            OccurredAt = at,
            Type = second.Type,
            MrrBefore = second.MrrBefore,
            MrrAfter = second.MrrAfter,
            Source = "test",
            StripeEventId = "later",
            CreatedAt = first.CreatedAt.AddSeconds(1),
        };

        RevenueLedgerRules.MrrAt([later, first], at).Should().Be(79m);
        RevenueLedgerRules.MrrAt([first, later], at).Should().Be(79m);
    }

    [Fact]
    public void MrrAt_SumsAcrossSubscriptions()
    {
        List<SubscriptionRevenueEvent> events =
        [
            Event(SubA, RevenueEventType.New, Utc(2026, 2, 1), 0m, 59m),
            Event(SubB, RevenueEventType.New, Utc(2026, 2, 5), 0m, 29m),
            Event(SubB, RevenueEventType.Churn, Utc(2026, 4, 1), 29m, 0m),
        ];

        RevenueLedgerRules.MrrAt(events, Utc(2026, 3, 31)).Should().Be(88m);
        RevenueLedgerRules.MrrAt(events, Utc(2026, 4, 30)).Should().Be(59m);
    }

    [Fact]
    public void MovementsFor_OneOfEachMovementType_SignsAndNetAreCorrect()
    {
        List<SubscriptionRevenueEvent> events =
        [
            Event(SubA, RevenueEventType.New, Utc(2026, 3, 2), 0m, 59m),
            Event(SubA, RevenueEventType.Expansion, Utc(2026, 3, 10), 59m, 79m),
            Event(SubB, RevenueEventType.Reactivation, Utc(2026, 3, 12), 0m, 30m),
            Event(SubB, RevenueEventType.Contraction, Utc(2026, 3, 20), 79m, 69m),
            Event(SubA, RevenueEventType.Churn, Utc(2026, 3, 25), 49.17m, 0m),
        ];

        MrrMovementTotals totals = RevenueLedgerRules.MovementsFor(events);

        totals.New.Should().Be(59m);
        totals.Expansion.Should().Be(20m);
        totals.Reactivation.Should().Be(30m);
        totals.Contraction.Should().Be(-10m);
        totals.Churn.Should().Be(-49.17m);
        totals.Net.Should().Be(59m + 20m + 30m - 10m - 49.17m);
    }

    [Fact]
    public void MovementsFor_StateOnlyEvents_AreExcluded()
    {
        List<SubscriptionRevenueEvent> events =
        [
            Event(SubA, RevenueEventType.Paused, Utc(2026, 5, 12), 59m, 59m),
            Event(SubA, RevenueEventType.Resumed, Utc(2026, 5, 20), 59m, 59m),
            Event(SubA, RevenueEventType.PastDue, Utc(2026, 5, 21), 59m, 59m),
            Event(SubA, RevenueEventType.Recovered, Utc(2026, 5, 22), 59m, 59m),
        ];

        MrrMovementTotals totals = RevenueLedgerRules.MovementsFor(events);

        totals.Should().Be(new MrrMovementTotals(0m, 0m, 0m, 0m, 0m, 0m));
    }

    [Fact]
    public void GrowthMonthlyFebToMay_MatchesSpecWorkedExample()
    {
        // Growth monthly 59 from 1 Feb; upgrade to Premium 79 on 10 Mar; scheduled downgrade
        // back to Growth lands 10 May.
        List<SubscriptionRevenueEvent> events =
        [
            Event(SubA, RevenueEventType.New, Utc(2026, 2, 1), 0m, 59m),
            Event(SubA, RevenueEventType.Expansion, Utc(2026, 3, 10), 59m, 79m),
            Event(SubA, RevenueEventType.Contraction, Utc(2026, 5, 10), 79m, 59m),
        ];

        RevenueLedgerRules.MrrAt(events, MrrRules.EndOfMonth(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc))).Should().Be(59m);
        RevenueLedgerRules.MrrAt(events, MrrRules.EndOfMonth(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc))).Should().Be(79m);
        RevenueLedgerRules.MrrAt(events, MrrRules.EndOfMonth(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc))).Should().Be(79m);
        RevenueLedgerRules.MrrAt(events, MrrRules.EndOfMonth(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc))).Should().Be(59m);

        RevenueLedgerRules.MovementsFor(events.Where(e => e.OccurredAt.Month == 3)).Expansion.Should().Be(20m);
        RevenueLedgerRules.MovementsFor(events.Where(e => e.OccurredAt.Month == 5)).Contraction.Should().Be(-20m);
        RevenueLedgerRules.MovementsFor(events.Where(e => e.OccurredAt.Month == 4)).Net.Should().Be(0m);
    }

    [Fact]
    public void LedgerMrrAtNow_EqualsMrrRulesMrrAtNow_ToTheCent()
    {
        DateTime now = DateTime.UtcNow;
        DateTime signedUp = now.AddMonths(-5);

        static Subscription Sub(SubscriptionStatus status, BillingInterval interval, decimal billed, decimal? discount = null) => new()
        {
            StudioId = Guid.NewGuid(),
            PlanId = Guid.NewGuid(),
            Status = status,
            BillingInterval = interval,
            BilledUnitAmount = billed,
            BilledQuantity = 1,
            BilledCurrency = "eur",
            RecurringDiscountPercent = discount,
            CreatedAt = DateTime.UtcNow.AddMonths(-5),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(10),
        };

        Subscription activeMonthly = Sub(SubscriptionStatus.Active, BillingInterval.Monthly, 59m);
        Subscription activeYearly = Sub(SubscriptionStatus.Active, BillingInterval.Yearly, 590m);       // 49.1666...
        Subscription activeDiscounted = Sub(SubscriptionStatus.Active, BillingInterval.Monthly, 79m, discount: 33m);
        Subscription pastDue = Sub(SubscriptionStatus.PastDue, BillingInterval.Monthly, 79m);
        Subscription suspended = Sub(SubscriptionStatus.Active, BillingInterval.Monthly, 29m);
        Subscription cancelled = Sub(SubscriptionStatus.Cancelled, BillingInterval.Monthly, 49m);
        Subscription free = Sub(SubscriptionStatus.Active, BillingInterval.Monthly, 0m);

        List<SubscriptionRevenueInput> inputs =
        [
            new(activeMonthly, signedUp, null, true, null),
            new(activeYearly, signedUp, null, true, null),
            new(activeDiscounted, signedUp, null, true, null),
            new(pastDue, signedUp, null, true, null),
            new(suspended, signedUp, null, false, now.AddDays(-9)),
            new(cancelled, signedUp, now.AddDays(-20), true, null),
            new(free, signedUp, null, true, null),
        ];

        // A fully up-to-date ledger: each subscription's history ends in the state MrrRules sees.
        SubscriptionRevenueEvent Row(Subscription s, RevenueEventType type, decimal before, decimal after, int daysAgo) =>
            new()
            {
                SubscriptionId = s.Id,
                StudioId = s.StudioId,
                OccurredAt = now.AddDays(-daysAgo),
                Type = type,
                MrrBefore = before,
                MrrAfter = after,
                Source = "test",
                StripeEventId = Guid.NewGuid().ToString(),
            };

        List<SubscriptionRevenueEvent> events =
        [
            Row(activeMonthly, RevenueEventType.New, 0m, MrrRules.MonthlyEquivalent(activeMonthly), 100),
            Row(activeYearly, RevenueEventType.New, 0m, MrrRules.MonthlyEquivalent(activeYearly), 90),
            Row(activeDiscounted, RevenueEventType.New, 0m, MrrRules.MonthlyEquivalent(activeDiscounted), 80),
            Row(pastDue, RevenueEventType.New, 0m, MrrRules.MonthlyEquivalent(pastDue), 70),
            Row(pastDue, RevenueEventType.PastDue, MrrRules.MonthlyEquivalent(pastDue), MrrRules.MonthlyEquivalent(pastDue), 5),
            Row(suspended, RevenueEventType.New, 0m, MrrRules.MonthlyEquivalent(suspended), 60),
            Row(suspended, RevenueEventType.Paused, MrrRules.MonthlyEquivalent(suspended), MrrRules.MonthlyEquivalent(suspended), 9),
            Row(cancelled, RevenueEventType.New, 0m, MrrRules.MonthlyEquivalent(cancelled), 50),
            Row(cancelled, RevenueEventType.Churn, MrrRules.MonthlyEquivalent(cancelled), 0m, 20),
        ];

        decimal fromRules = MrrRules.MrrAt(inputs, now, now);
        decimal fromLedger = RevenueLedgerRules.MrrAt(events, now);

        fromLedger.Should().Be(fromRules);
        fromRules.Should().BeGreaterThan(0m); // guard against a vacuous 0 == 0
    }
}
