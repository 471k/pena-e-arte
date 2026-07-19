using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Persistence.Seed;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Infrastructure;

public class DataSeederPlanReconciliationTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    [Fact]
    public async Task ReconcileCorePlansAsync_EmptyDatabase_InsertsAllFiveCanonicalPlans()
    {
        // Act
        await DataSeeder.ReconcileCorePlansAsync(_db);

        // Assert
        _db.Plans.Should().HaveCount(5);
        _db.Plans.Select(p => p.Name).Should()
            .Contain(["Starter", "Growth", "Premium", "Pro"]);
        _db.Plans.Count(p => p.Name == "Premium").Should().Be(2);
    }

    [Fact]
    public async Task ReconcileCorePlansAsync_StaleStarterMissingMaxFields_BackfillsThem()
    {
        // Arrange — exact stale shape from the bug report: correct price, null limits
        _db.Plans.Add(new Plan
        {
            Id                    = DataSeeder.StarterPlanId,
            Name                  = "Starter",
            BillingInterval       = BillingInterval.Monthly,
            PriceMonthly          = 29m,
            PriceYearly           = 290m,
            YearlyDiscountPercent = 17,
            MaxArtists            = null,
            MaxAppointmentsPerMonth = null,
            MaxStorageGb          = null,
        });
        await _db.SaveChangesAsync();

        // Act
        await DataSeeder.ReconcileCorePlansAsync(_db);

        // Assert
        Plan starter = _db.Plans.Single(p => p.Id == DataSeeder.StarterPlanId);
        starter.MaxArtists.Should().Be(1);
        starter.MaxAppointmentsPerMonth.Should().Be(40);
        starter.MaxNotificationsPerMonth.Should().Be(150);
        starter.MaxStorageGb.Should().Be(2);
        starter.MaxLocations.Should().Be(1);
        // Price was already correct — must not have changed
        starter.PriceMonthly.Should().Be(29m);
    }

    [Fact]
    public async Task ReconcileCorePlansAsync_OnlyStalePremiumYearlyRowExists_CorrectsItAndInsertsMissingMonthlyRow()
    {
        // Arrange — reproduces the exact bug: one Premium row at the Yearly id, with
        // the old placeholder pricing and no Max* fields, and NO row at all for the
        // Monthly id.
        _db.Plans.Add(new Plan
        {
            Id                    = DataSeeder.PremiumYearlyPlanId,
            Name                  = "Premium",
            BillingInterval       = BillingInterval.Yearly,
            PriceMonthly          = 30m,
            PriceYearly           = 200m,
            YearlyDiscountPercent = 44,
            MaxArtists            = null,
            MaxAppointmentsPerMonth = null,
            MaxStorageGb          = null,
            PairedPlanId          = null,
        });
        await _db.SaveChangesAsync();

        // Act
        await DataSeeder.ReconcileCorePlansAsync(_db);

        // Assert — exactly two Premium rows now exist
        List<Plan> premiumRows = _db.Plans.Where(p => p.Name == "Premium").ToList();
        premiumRows.Should().HaveCount(2);

        Plan yearly = premiumRows.Single(p => p.Id == DataSeeder.PremiumYearlyPlanId);
        yearly.PriceMonthly.Should().Be(79m);
        yearly.PriceYearly.Should().Be(790m);
        yearly.YearlyDiscountPercent.Should().Be(17);
        yearly.MaxArtists.Should().Be(6);
        yearly.MaxAppointmentsPerMonth.Should().Be(400);
        yearly.MaxStorageGb.Should().Be(25);
        yearly.PairedPlanId.Should().Be(DataSeeder.PremiumMonthlyPlanId);

        Plan monthly = premiumRows.Single(p => p.Id == DataSeeder.PremiumMonthlyPlanId);
        monthly.BillingInterval.Should().Be(BillingInterval.Monthly);
        monthly.PriceMonthly.Should().Be(79m);
        monthly.PriceYearly.Should().Be(790m);
        monthly.PairedPlanId.Should().Be(DataSeeder.PremiumYearlyPlanId);
    }

    [Fact]
    public async Task ReconcileCorePlansAsync_ProMissingMaxFields_BackfillsThemWithoutTouchingPrice()
    {
        _db.Plans.Add(new Plan
        {
            Id                    = DataSeeder.ProPlanId,
            Name                  = "Pro",
            BillingInterval       = BillingInterval.Monthly,
            PriceMonthly          = 99m,
            PriceYearly           = 990m,
            YearlyDiscountPercent = 17,
        });
        await _db.SaveChangesAsync();

        await DataSeeder.ReconcileCorePlansAsync(_db);

        Plan pro = _db.Plans.Single(p => p.Id == DataSeeder.ProPlanId);
        pro.MaxArtists.Should().Be(10);
        pro.MaxAppointmentsPerMonth.Should().Be(1000);
        pro.MaxNotificationsPerMonth.Should().Be(2500);
        pro.MaxStorageGb.Should().Be(50);
        pro.MaxLocations.Should().Be(10);
        pro.AllowApiAccess.Should().BeTrue();
        pro.PrioritySupport.Should().BeTrue();
        pro.PriceMonthly.Should().Be(99m);
    }

    [Fact]
    public async Task ReconcileCorePlansAsync_CalledTwice_IsIdempotent()
    {
        await DataSeeder.ReconcileCorePlansAsync(_db);
        await DataSeeder.ReconcileCorePlansAsync(_db);

        _db.Plans.Should().HaveCount(5);
        _db.Plans.Count(p => p.Id == DataSeeder.PremiumMonthlyPlanId).Should().Be(1);
        _db.Plans.Count(p => p.Id == DataSeeder.PremiumYearlyPlanId).Should().Be(1);
    }

    [Fact]
    public async Task ReconcileCorePlansAsync_DoesNotTouchUnrelatedPlanRows()
    {
        // A hand-created Plan (e.g. issuer-cloned custom tier) with an Id outside the
        // five canonical constants must be left completely alone.
        Guid customPlanId = Guid.NewGuid();
        _db.Plans.Add(new Plan
        {
            Id                    = customPlanId,
            Name                  = "Studio X Custom Deal",
            BillingInterval       = BillingInterval.Monthly,
            PriceMonthly          = 149m,
            PriceYearly           = 1490m,
            YearlyDiscountPercent = 17,
            MaxArtists            = 20,
        });
        await _db.SaveChangesAsync();

        await DataSeeder.ReconcileCorePlansAsync(_db);

        _db.Plans.Should().HaveCount(6); // 5 canonical + 1 untouched custom
        Plan custom = _db.Plans.Single(p => p.Id == customPlanId);
        custom.Name.Should().Be("Studio X Custom Deal");
        custom.PriceMonthly.Should().Be(149m);
        custom.MaxArtists.Should().Be(20);
    }
}
