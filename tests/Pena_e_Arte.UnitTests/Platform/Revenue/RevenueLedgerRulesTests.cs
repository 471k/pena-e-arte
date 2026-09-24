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
}
