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
    public async Task ReconcileCoreTiersAsync_EmptyDatabase_InsertsAllFiveCanonicalPlans()
    {
        await DataSeeder.ReconcileCoreTiersAsync(_db);

        _db.Plans.Should().HaveCount(5);
        _db.Plans.Select(p => p.Name).Should()
            .Contain(["Free", "Starter", "Growth", "Premium", "Pro"]);
        _db.Plans.Count(p => p.Name == "Premium").Should().Be(1);
    }

    [Fact]
    public async Task ReconcileCoreTiersAsync_EmptyDatabase_InsertsSixPlanPriceRows()
    {
        // 5 tiers, one Monthly row each, plus Premium's extra Yearly row = 6.
        await DataSeeder.ReconcileCoreTiersAsync(_db);

        _db.PlanPrices.Should().HaveCount(6);
        _db.PlanPrices.Count(pp => pp.PlanId == DataSeeder.PremiumPlanId).Should().Be(2);
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
        starter.MaxLocations.Should().Be(1);
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

        _db.PlanPrices.Single(pp => pp.PlanId == DataSeeder.StarterPlanId).Price.Should().Be(29m);
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

        _db.PlanPrices.Single(pp => pp.PlanId == DataSeeder.StarterPlanId)
            .StripePriceId.Should().Be("price_real_stripe_id");
    }

    [Fact]
    public async Task ReconcileCoreTiersAsync_ProMissingMaxFields_BackfillsThemWithoutTouchingPrice()
    {
        _db.Plans.Add(new Plan
        {
            Id = DataSeeder.ProPlanId,
            Name = "Pro",
            YearlyDiscountPercent = 17,
        });
        await _db.SaveChangesAsync();

        await DataSeeder.ReconcileCoreTiersAsync(_db);

        Plan pro = _db.Plans.Single(p => p.Id == DataSeeder.ProPlanId);
        pro.MaxArtists.Should().Be(10);
        pro.MaxAppointmentsPerMonth.Should().Be(1000);
        pro.MaxNotificationsPerMonth.Should().Be(2500);
        pro.MaxStorageGb.Should().Be(50);
        pro.MaxLocations.Should().Be(10);
        pro.AllowApiAccess.Should().BeTrue();
        pro.PrioritySupport.Should().BeTrue();
        _db.PlanPrices.Single(pp => pp.PlanId == DataSeeder.ProPlanId).Price.Should().Be(99m);
    }

    [Fact]
    public async Task ReconcileCoreTiersAsync_CalledTwice_IsIdempotent()
    {
        await DataSeeder.ReconcileCoreTiersAsync(_db);
        await DataSeeder.ReconcileCoreTiersAsync(_db);

        _db.Plans.Should().HaveCount(5);
        _db.PlanPrices.Should().HaveCount(6);
        _db.Plans.Count(p => p.Id == DataSeeder.PremiumPlanId).Should().Be(1);
    }

    [Fact]
    public async Task ReconcileCoreTiersAsync_DoesNotTouchDifferentlyNamedCustomPlan()
    {
        // A hand-created Plan (e.g. admin-cloned custom tier) not matching any of the
        // five reserved tier names must be left completely alone — this reconciler is
        // keyed on tier Name, so there is nowhere for a duplicate/orphan of a RESERVED
        // name to hide (the bug class both prior fixes had to clean up after).
        Guid customPlanId = Guid.NewGuid();
        Plan custom = new() { Id = customPlanId, Name = "Studio X Custom Deal", MaxArtists = 20 };
        custom.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 149m });
        _db.Plans.Add(custom);
        await _db.SaveChangesAsync();

        await DataSeeder.ReconcileCoreTiersAsync(_db);

        _db.Plans.Should().HaveCount(6); // 5 canonical + 1 untouched custom
        Plan stored = _db.Plans.Single(p => p.Id == customPlanId);
        stored.Name.Should().Be("Studio X Custom Deal");
        stored.MaxArtists.Should().Be(20);
        _db.PlanPrices.Single(pp => pp.PlanId == customPlanId).Price.Should().Be(149m);
    }
}
