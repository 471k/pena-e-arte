using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Platform.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

public class BackfillSubscriptionBilledAmountsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IStripeBillingService _stripe = Substitute.For<IStripeBillingService>();

    private BackfillSubscriptionBilledAmountsHandler CreateSut() =>
        new(_db, _stripe, NullLogger<BackfillSubscriptionBilledAmountsHandler>.Instance);

    [Fact]
    public async Task Handle_CardBilledSubscriptionWithNullSnapshot_PopulatesFromStripe()
    {
        Guid studioId = Guid.NewGuid();
        _db.Subscriptions.Add(new Subscription
        {
            StudioId = studioId,
            Status = SubscriptionStatus.Active,
            StripeSubscriptionId = "sub_card1",
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(10),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        _stripe.GetSubscriptionBilledPriceAsync("sub_card1", Arg.Any<CancellationToken>())
            .Returns(new StripePriceInfo(true, 7900, "eur", "month", 1));

        BackfillSubscriptionBilledAmountsResponse result =
            await CreateSut().Handle(new BackfillSubscriptionBilledAmountsCommand(), default);

        result.CardBilledUpdated.Should().Be(1);
        Subscription stored = _db.Subscriptions.Single(s => s.StudioId == studioId);
        stored.BilledUnitAmount.Should().Be(79m);
        stored.BilledCurrency.Should().Be("eur");
    }

    [Fact]
    public async Task Handle_CardBilledSubscription_IsIdempotentOnRerun()
    {
        Guid studioId = Guid.NewGuid();
        _db.Subscriptions.Add(new Subscription
        {
            StudioId = studioId,
            Status = SubscriptionStatus.Active,
            StripeSubscriptionId = "sub_card2",
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(10),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        _stripe.GetSubscriptionBilledPriceAsync("sub_card2", Arg.Any<CancellationToken>())
            .Returns(new StripePriceInfo(true, 7900, "eur", "month", 1));

        await CreateSut().Handle(new BackfillSubscriptionBilledAmountsCommand(), default);
        BackfillSubscriptionBilledAmountsResponse second =
            await CreateSut().Handle(new BackfillSubscriptionBilledAmountsCommand(), default);

        second.CardBilledUpdated.Should().Be(0); // already snapshotted — untouched on rerun
        await _stripe.Received(1).GetSubscriptionBilledPriceAsync("sub_card2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CashBilledSubscriptionWithNullSnapshot_PopulatesFromMonthlyPriceAndListsIt()
    {
        Guid studioId = Guid.NewGuid();
        Plan plan = new() { Name = "Pro" };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 49m });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();

        _db.Subscriptions.Add(new Subscription
        {
            StudioId = studioId,
            PlanId = plan.Id,
            Plan = plan,
            Status = SubscriptionStatus.Active,
            StripeSubscriptionId = null,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(10),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        BackfillSubscriptionBilledAmountsResponse result =
            await CreateSut().Handle(new BackfillSubscriptionBilledAmountsCommand(), default);

        result.CashBilledSnapshots.Should().ContainSingle(s => s.StudioId == studioId && s.Price == 49m);
        _db.Subscriptions.Single(s => s.StudioId == studioId).BilledUnitAmount.Should().Be(49m);
    }

    [Fact]
    public async Task Handle_AlreadySnapshotted_UntouchedAndNotListed()
    {
        Guid studioId = Guid.NewGuid();
        _db.Subscriptions.Add(new Subscription
        {
            StudioId = studioId,
            Status = SubscriptionStatus.Active,
            StripeSubscriptionId = null,
            BilledUnitAmount = 79m,
            BilledCurrency = "eur",
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(10),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        BackfillSubscriptionBilledAmountsResponse result =
            await CreateSut().Handle(new BackfillSubscriptionBilledAmountsCommand(), default);

        result.CashBilledSnapshots.Should().BeEmpty();
        result.CardBilledUpdated.Should().Be(0);
    }
}
