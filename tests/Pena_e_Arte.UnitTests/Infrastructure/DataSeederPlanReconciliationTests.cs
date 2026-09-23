using FluentAssertions;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Persistence.Seed;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Infrastructure;

public class DataSeederPlanReconciliationTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    [Fact]
    public async Task ReconcileCoreTiersAsync_EmptyDatabase_InsertsFourCanonicalPlansNoPro()
    {
        await DataSeeder.ReconcileCoreTiersAsync(_db);

        _db.Plans.Should().HaveCount(4);
        _db.Plans.Select(p => p.Name).Should()
            .Contain(["Free", "Starter", "Growth", "Premium"]);
        _db.Plans.Select(p => p.Name).Should().NotContain("Pro");
        _db.Plans.Any(p => p.Id == DataSeeder.ProPlanId).Should().BeFalse();
    }

    [Fact]
    public async Task ReconcileCoreTiersAsync_EmptyDatabase_InsertsSevenPlanPriceRows()
    {
        // Free 1, Starter 2, Growth 2, Premium 2 = 7.
        await DataSeeder.ReconcileCoreTiersAsync(_db);

        _db.PlanPrices.Should().HaveCount(7);
        _db.PlanPrices.Count(pp => pp.PlanId == DataSeeder.FreePlanId).Should().Be(1);
        _db.PlanPrices.Count(pp => pp.PlanId == DataSeeder.StarterPlanId).Should().Be(2);
        _db.PlanPrices.Count(pp => pp.PlanId == DataSeeder.GrowthPlanId).Should().Be(2);
        _db.PlanPrices.Count(pp => pp.PlanId == DataSeeder.PremiumPlanId).Should().Be(2);
    }

    [Fact]
    public async Task ReconcileCoreTiersAsync_EmptyDatabase_PremiumAbsorbsProLimitsAndApiAccess()
    {
        await DataSeeder.ReconcileCoreTiersAsync(_db);

        Plan premium = _db.Plans.Single(p => p.Id == DataSeeder.PremiumPlanId);
        premium.MaxArtists.Should().Be(10);
        premium.MaxAppointmentsPerMonth.Should().Be(1000);
        premium.MaxNotificationsPerMonth.Should().Be(2500);
        premium.MaxStorageGb.Should().Be(50);
        premium.MaxLocations.Should().BeNull();
        premium.AllowApiAccess.Should().BeTrue();
        premium.PrioritySupport.Should().BeFalse();
    }

    [Fact]
    public async Task ReconcileCoreTiersAsync_StaleStarterMissingMaxFields_BackfillsThem()
    {
        _db.Plans.Add(new Plan
        {
            Id = DataSeeder.StarterPlanId,
            Name = "Starter",
            YearlyDiscountPercent = 17,
            MaxArtists = null,
            MaxAppointmentsPerMonth = null,
            MaxStorageGb = null,
        });
        await _db.SaveChangesAsync();

        await DataSeeder.ReconcileCoreTiersAsync(_db);

        Plan starter = _db.Plans.Single(p => p.Id == DataSeeder.StarterPlanId);
        starter.MaxArtists.Should().Be(1);
        starter.MaxAppointmentsPerMonth.Should().Be(40);
        starter.MaxNotificationsPerMonth.Should().Be(150);
        starter.MaxStorageGb.Should().Be(2);
        starter.MaxLocations.Should().BeNull();
    }

    [Fact]
    public async Task ReconcileCoreTiersAsync_ExistingPlanPriceDrift_CorrectsPrice()
    {
        _db.Plans.Add(new Plan { Id = DataSeeder.StarterPlanId, Name = "Starter" });
        _db.PlanPrices.Add(new PlanPrice
        {
            PlanId = DataSeeder.StarterPlanId,
            Interval = BillingInterval.Monthly,
            Price = 19m, // stale/drifted price
        });
        await _db.SaveChangesAsync();

        await DataSeeder.ReconcileCoreTiersAsync(_db);

        _db.PlanPrices.Single(pp => pp.PlanId == DataSeeder.StarterPlanId && pp.Interval == BillingInterval.Monthly)
            .Price.Should().Be(29m);
    }

    [Fact]
    public async Task ReconcileCoreTiersAsync_ExistingPlanPrice_DoesNotOverwriteStripePriceId()
    {
        _db.Plans.Add(new Plan { Id = DataSeeder.StarterPlanId, Name = "Starter" });
        _db.PlanPrices.Add(new PlanPrice
        {
            PlanId = DataSeeder.StarterPlanId,
            Interval = BillingInterval.Monthly,
            Price = 29m,
            StripePriceId = "price_real_stripe_id",
        });
        await _db.SaveChangesAsync();

        await DataSeeder.ReconcileCoreTiersAsync(_db);

        _db.PlanPrices.Single(pp => pp.PlanId == DataSeeder.StarterPlanId && pp.Interval == BillingInterval.Monthly)
            .StripePriceId.Should().Be("price_real_stripe_id");
    }

    [Fact]
    public async Task ReconcileCoreTiersAsync_ExistingYearlyPlanPrice_DoesNotOverwriteStripePriceId()
    {
        _db.Plans.Add(new Plan { Id = DataSeeder.StarterPlanId, Name = "Starter" });
        _db.PlanPrices.Add(new PlanPrice
        {
            PlanId = DataSeeder.StarterPlanId,
            Interval = BillingInterval.Yearly,
            Price = 290m,
            StripePriceId = "price_real_yearly_stripe_id",
        });
        await _db.SaveChangesAsync();

        await DataSeeder.ReconcileCoreTiersAsync(_db);

        _db.PlanPrices.Single(pp => pp.PlanId == DataSeeder.StarterPlanId && pp.Interval == BillingInterval.Yearly)
            .StripePriceId.Should().Be("price_real_yearly_stripe_id");
    }

    [Fact]
    public async Task ReconcileCoreTiersAsync_ExistingProRowWithPrice_KeepsRowDeactivatesPriceLeavesValueUntouched()
    {
        _db.Plans.Add(new Plan { Id = DataSeeder.ProPlanId, Name = "Pro", YearlyDiscountPercent = 17 });
        _db.PlanPrices.Add(new PlanPrice
        {
            PlanId = DataSeeder.ProPlanId,
            Interval = BillingInterval.Monthly,
            Price = 99m,
            IsActive = true,
        });
        await _db.SaveChangesAsync();

        await DataSeeder.ReconcileCoreTiersAsync(_db);

        Plan pro = _db.Plans.Single(p => p.Id == DataSeeder.ProPlanId);
        pro.Name.Should().Be("Pro");

        PlanPrice proPrice = _db.PlanPrices.Single(pp => pp.PlanId == DataSeeder.ProPlanId);
        proPrice.IsActive.Should().BeFalse();
        proPrice.Price.Should().Be(99m); // untouched — reconciler no longer manages Pro's price value
    }

    [Fact]
    public async Task ReconcileCoreTiersAsync_CalledTwice_IsIdempotent()
    {
        await DataSeeder.ReconcileCoreTiersAsync(_db);
        await DataSeeder.ReconcileCoreTiersAsync(_db);

        _db.Plans.Should().HaveCount(4);
        _db.PlanPrices.Should().HaveCount(7);
        _db.Plans.Count(p => p.Id == DataSeeder.PremiumPlanId).Should().Be(1);
    }

    [Fact]
    public async Task ReconcileCoreTiersAsync_CalledTwiceWithRetiredPro_StaysIdempotent()
    {
        _db.Plans.Add(new Plan { Id = DataSeeder.ProPlanId, Name = "Pro" });
        _db.PlanPrices.Add(new PlanPrice
        {
            PlanId = DataSeeder.ProPlanId,
            Interval = BillingInterval.Monthly,
            Price = 99m,
        });
        await _db.SaveChangesAsync();

        await DataSeeder.ReconcileCoreTiersAsync(_db);
        await DataSeeder.ReconcileCoreTiersAsync(_db);

        _db.PlanPrices.Count(pp => pp.PlanId == DataSeeder.ProPlanId).Should().Be(1);
        _db.PlanPrices.Single(pp => pp.PlanId == DataSeeder.ProPlanId).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ReconcileCoreTiersAsync_DoesNotTouchDifferentlyNamedCustomPlan()
    {
        // A hand-created Plan (e.g. admin-cloned custom tier) not matching any of the
        // four reserved tier names must be left completely alone — this reconciler is
        // keyed on tier Name, so there is nowhere for a duplicate/orphan of a RESERVED
        // name to hide (the bug class both prior fixes had to clean up after).
        Guid customPlanId = Guid.NewGuid();
        Plan custom = new() { Id = customPlanId, Name = "Studio X Custom Deal", MaxArtists = 20 };
        custom.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 149m });
        _db.Plans.Add(custom);
        await _db.SaveChangesAsync();

        await DataSeeder.ReconcileCoreTiersAsync(_db);

        _db.Plans.Should().HaveCount(5); // 4 canonical + 1 untouched custom
        Plan stored = _db.Plans.Single(p => p.Id == customPlanId);
        stored.Name.Should().Be("Studio X Custom Deal");
        stored.MaxArtists.Should().Be(20);
        _db.PlanPrices.Single(pp => pp.PlanId == customPlanId).Price.Should().Be(149m);
    }
}
