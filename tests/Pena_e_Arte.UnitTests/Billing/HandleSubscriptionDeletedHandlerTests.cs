using FluentAssertions;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class HandleSubscriptionDeletedHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private HandleSubscriptionDeletedHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_KnownSubscription_SetsStatusToCancelled()
    {
        string stripeSubId = "sub_del123";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Active);

        await CreateSut().Handle(new HandleSubscriptionDeletedCommand(stripeSubId), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .Status.Should().Be(SubscriptionStatus.Cancelled);
    }

    [Fact]
    public async Task Handle_UnknownSubscription_DoesNotThrow()
    {
        Func<Task> act = () => CreateSut().Handle(
            new HandleSubscriptionDeletedCommand("sub_unknown"), default);

        await act.Should().NotThrowAsync();
    }

    private async Task SeedSubscription(string stripeSubId, SubscriptionStatus status)
    {
        _db.Subscriptions.Add(new Subscription
        {
            StudioId = Guid.NewGuid(),
            StripeSubscriptionId = stripeSubId,
            Status = status,
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
            GracePeriodEnd = DateTime.UtcNow.AddDays(21)
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    // ── Revenue ledger ────────────────────────────────────────────────────

    private async Task SeedPaid(string stripeSubId, SubscriptionStatus status)
    {
        _db.Subscriptions.Add(new Subscription
        {
            StudioId = Guid.NewGuid(),
            StripeSubscriptionId = stripeSubId,
            Status = status,
            BillingInterval = BillingInterval.Monthly,
            BilledUnitAmount = 59m,
            BilledQuantity = 1,
            BilledCurrency = "eur",
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(10),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.PastDue)]
    public async Task Handle_BillingSubscription_WritesChurnToZero(SubscriptionStatus status)
    {
        await SeedPaid("sub_churn", status);

        await CreateSut().Handle(new HandleSubscriptionDeletedCommand("sub_churn", "evt_del"), default);

        SubscriptionRevenueEvent ledgerEvent = _db.SubscriptionRevenueEvents.Single();
        ledgerEvent.Type.Should().Be(RevenueEventType.Churn);
        ledgerEvent.MrrBefore.Should().Be(59m);
        ledgerEvent.MrrAfter.Should().Be(0m);
        ledgerEvent.StripeEventId.Should().Be("evt_del");
    }

    [Fact]
    public async Task Handle_SameWebhookDeliveredTwice_WritesOneChurn()
    {
        await SeedPaid("sub_twice", SubscriptionStatus.Active);

        await CreateSut().Handle(new HandleSubscriptionDeletedCommand("sub_twice", "evt_del"), default);
        _db.ChangeTracker.Clear();
        await CreateSut().Handle(new HandleSubscriptionDeletedCommand("sub_twice", "evt_del"), default);

        _db.SubscriptionRevenueEvents.Count().Should().Be(1);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Trialing)]
    [InlineData(SubscriptionStatus.GracePeriod)]
    [InlineData(SubscriptionStatus.Cancelled)]
    public async Task Handle_SubscriptionThatWasNotBilling_WritesNoChurn(SubscriptionStatus status)
    {
        await SeedPaid("sub_not_billing", status);

        await CreateSut().Handle(new HandleSubscriptionDeletedCommand("sub_not_billing"), default);

        _db.SubscriptionRevenueEvents.Should().BeEmpty();
    }
}
