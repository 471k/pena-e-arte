using FluentAssertions;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.ConsentForms;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

public class GetMrrHistoryHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly CapturingLogger<GetMrrHistoryHandler> _logger = new();

    private GetMrrHistoryHandler CreateSut() => new(_db, _logger);

    [Fact]
    public async Task Handle_NoSubscriptions_ReturnsZeroMrrForEveryMonth()
    {
        List<MrrDataPointResponse> result = await CreateSut().Handle(new GetMrrHistoryQuery(3), default);

        result.Should().HaveCount(3);
        result.Should().OnlyContain(p => p.Mrr == 0m);
    }

    [Fact]
    public async Task Handle_MonthsClamped_StaysWithinOneToTwentyFour()
    {
        List<MrrDataPointResponse> result = await CreateSut().Handle(new GetMrrHistoryQuery(999), default);

        result.Should().HaveCount(24);
    }

    [Fact]
    public async Task Handle_MonthlySubscription_ContributesFullPriceToCurrentMonth()
    {
        Studio studio = SeedStudio();
        Plan plan = new() { Name = "Pro" };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 49m });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();

        _db.Subscriptions.Add(new Subscription
        {
            StudioId = studio.Id,
            PlanId = plan.Id,
            BillingInterval = BillingInterval.Monthly,
            Status = SubscriptionStatus.Active,
            CreatedAt = DateTime.UtcNow.AddDays(-5),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(25),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        List<MrrDataPointResponse> result = await CreateSut().Handle(new GetMrrHistoryQuery(1), default);

        result.Single().Mrr.Should().Be(49m);
    }

    [Fact]
    public async Task Handle_YearlySubscription_ContributesMonthlyEquivalent()
    {
        // Regression test for the confirmed pre-existing revenue-reporting bug — same
        // fix as GetPlatformStatsHandler's MRR calculation.
        Studio studio = SeedStudio();
        Plan plan = new() { Name = "Premium" };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 79m });
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Yearly, Price = 790m });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();

        _db.Subscriptions.Add(new Subscription
        {
            StudioId = studio.Id,
            PlanId = plan.Id,
            BillingInterval = BillingInterval.Yearly,
            Status = SubscriptionStatus.Active,
            CreatedAt = DateTime.UtcNow.AddDays(-5),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(360),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        List<MrrDataPointResponse> result = await CreateSut().Handle(new GetMrrHistoryQuery(1), default);

        result.Single().Mrr.Should().BeApproximately(790m / 12m, 0.01m);
    }

    [Fact]
    public async Task Handle_SubscriptionCreatedTwoMonthsAgo_ZeroBeforeCreation_FullAfter()
    {
        DateTime monthStart = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime twoMonthsAgoStart = monthStart.AddMonths(-2);
        Studio studio = SeedStudio(trialExpiresAt: twoMonthsAgoStart.AddDays(-30));
        Plan plan = new() { Name = "Premium" };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 79m });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();

        _db.Subscriptions.Add(new Subscription
        {
            StudioId = studio.Id,
            PlanId = plan.Id,
            BillingInterval = BillingInterval.Monthly,
            Status = SubscriptionStatus.Active,
            CreatedAt = twoMonthsAgoStart.AddMonths(1).AddDays(5), // created last month
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        List<MrrDataPointResponse> result = await CreateSut().Handle(new GetMrrHistoryQuery(3), default);

        result[0].Mrr.Should().Be(0m);   // two months ago — not yet created
        result[1].Mrr.Should().Be(79m);  // last month — created mid-month, billing by month end
        result[2].Mrr.Should().Be(79m);  // current month
    }

    [Fact]
    public async Task Handle_AdminCancelledLastMonth_ZeroFromCancellationOnward()
    {
        DateTime monthStart = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime lastMonthStart = monthStart.AddMonths(-1);
        Studio studio = SeedStudio(trialExpiresAt: lastMonthStart.AddMonths(-3));
        Plan plan = new() { Name = "Premium" };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 79m });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();

        Subscription subscription = new()
        {
            StudioId = studio.Id,
            PlanId = plan.Id,
            BillingInterval = BillingInterval.Monthly,
            Status = SubscriptionStatus.Cancelled,
            CreatedAt = lastMonthStart.AddMonths(-2),
            CurrentPeriodEnd = monthStart.AddDays(10), // Stripe's future period end — must not be used
        };
        _db.Subscriptions.Add(subscription);
        await _db.SaveChangesAsync();

        AuditLogEntry cancelledEntry = AuditLogEntry.Create(
            Guid.NewGuid(), "Admin", AuditActions.SubscriptionCancelledByAdmin,
            AuditTargetTypes.Subscription, studio.Id, studio.Id, "{}");
        _db.AuditLogEntries.Add(cancelledEntry);
        await _db.SaveChangesAsync();
        _db.Entry(cancelledEntry).Property(e => e.CreatedAt).CurrentValue = lastMonthStart.AddDays(10);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        List<MrrDataPointResponse> result = await CreateSut().Handle(new GetMrrHistoryQuery(3), default);

        result[0].Mrr.Should().Be(79m); // two months ago — created, still billing
        result[1].Mrr.Should().Be(0m);  // last month — cancelled by admin partway through, before month end
        result[2].Mrr.Should().Be(0m);  // current month — Cancelled
    }

    [Fact]
    public async Task Handle_CurrentMonthPoint_MatchesPlatformStatsMrr_ToTheCent()
    {
        DateTime monthStart = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        Plan plan = new() { Name = "Premium" };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 79m });
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Yearly, Price = 790m });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();

        Studio monthlyActive = SeedStudio(trialExpiresAt: monthStart.AddMonths(-6));
        Studio yearlyActive = SeedStudio(trialExpiresAt: monthStart.AddMonths(-6));
        Studio pastDue = SeedStudio(trialExpiresAt: monthStart.AddMonths(-6));
        Studio cancelled = SeedStudio(trialExpiresAt: monthStart.AddMonths(-6));
        Studio free = SeedStudio(trialExpiresAt: monthStart.AddMonths(-6));
        Studio suspended = SeedStudio(isActive: false, trialExpiresAt: monthStart.AddMonths(-6));
        Studio trialing = SeedStudio(trialExpiresAt: DateTime.UtcNow.AddDays(7));
        await _db.SaveChangesAsync();

        Plan freePlan = new() { Name = "Free" };
        freePlan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 0m });
        _db.Plans.Add(freePlan);
        await _db.SaveChangesAsync();

        _db.Subscriptions.AddRange(
            new Subscription { StudioId = monthlyActive.Id, PlanId = plan.Id, BillingInterval = BillingInterval.Monthly, Status = SubscriptionStatus.Active, CreatedAt = monthStart.AddMonths(-3), CurrentPeriodEnd = DateTime.UtcNow.AddDays(20) },
            new Subscription { StudioId = yearlyActive.Id, PlanId = plan.Id, BillingInterval = BillingInterval.Yearly, Status = SubscriptionStatus.Active, CreatedAt = monthStart.AddMonths(-3), CurrentPeriodEnd = DateTime.UtcNow.AddMonths(9) },
            new Subscription { StudioId = pastDue.Id, PlanId = plan.Id, BillingInterval = BillingInterval.Monthly, Status = SubscriptionStatus.PastDue, CreatedAt = monthStart.AddMonths(-3), CurrentPeriodEnd = DateTime.UtcNow.AddDays(-5) },
            new Subscription { StudioId = cancelled.Id, PlanId = plan.Id, BillingInterval = BillingInterval.Monthly, Status = SubscriptionStatus.Cancelled, CreatedAt = monthStart.AddMonths(-3), CurrentPeriodEnd = DateTime.UtcNow.AddDays(-1) },
            new Subscription { StudioId = free.Id, PlanId = freePlan.Id, BillingInterval = BillingInterval.Monthly, Status = SubscriptionStatus.Active, CreatedAt = monthStart.AddMonths(-3), CurrentPeriodEnd = DateTime.UtcNow.AddDays(20) },
            new Subscription { StudioId = suspended.Id, PlanId = plan.Id, BillingInterval = BillingInterval.Monthly, Status = SubscriptionStatus.Active, CreatedAt = monthStart.AddMonths(-3), CurrentPeriodEnd = DateTime.UtcNow.AddDays(20) },
            new Subscription { StudioId = trialing.Id, PlanId = null, BillingInterval = BillingInterval.Monthly, Status = SubscriptionStatus.Trialing, CreatedAt = DateTime.UtcNow.AddDays(-7), CurrentPeriodEnd = DateTime.UtcNow.AddDays(7) });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        List<MrrDataPointResponse> historyResult = await CreateSut().Handle(new GetMrrHistoryQuery(1), default);
        GetPlatformStatsHandler statsHandler = new(_db, new CapturingLogger<GetPlatformStatsHandler>());
        PlatformStatsResponse statsResult = await statsHandler.Handle(new GetPlatformStatsQuery(), default);

        historyResult.Single().Mrr.Should().Be(statsResult.Mrr);
        statsResult.Mrr.Should().Be(79m + (790m / 12m)); // monthly + yearly active only
    }

    [Fact]
    public async Task Handle_FallbackSubscription_LogsFallbackCount()
    {
        Studio studio = SeedStudio();
        Plan plan = new() { Name = "Pro" };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 49m });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();

        _db.Subscriptions.Add(new Subscription
        {
            StudioId = studio.Id,
            PlanId = plan.Id,
            BillingInterval = BillingInterval.Monthly,
            Status = SubscriptionStatus.Active,
            CreatedAt = DateTime.UtcNow.AddMonths(-3),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(20),
            BilledUnitAmount = null,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateSut().Handle(new GetMrrHistoryQuery(1), default);

        _logger.Entries.Should().ContainSingle(e =>
            e.Message.Contains("1") && e.Message.Contains("fallback", StringComparison.OrdinalIgnoreCase));
    }

    private Studio SeedStudio(bool isActive = true, DateTime? trialExpiresAt = null)
    {
        Studio studio = new()
        {
            Name = $"Studio-{Guid.NewGuid():N}"[..20],
            Slug = Guid.NewGuid().ToString("N")[..20],
            City = "Porto",
            OwnerEmail = $"{Guid.NewGuid():N}@test.com",
            IsActive = isActive,
            TrialExpiresAt = trialExpiresAt ?? DateTime.UtcNow.AddDays(14),
        };
        _db.Studios.Add(studio);
        return studio;
    }
}
