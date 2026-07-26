using FluentAssertions;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class HandleInvoicePaidHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private HandleInvoicePaidHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_KnownSubscription_SetsStatusToActive()
    {
        string stripeSubId = "sub_abc123";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Trialing);
        DateTime periodEnd = DateTime.UtcNow.AddMonths(1);

        await CreateSut().Handle(new HandleInvoicePaidCommand(stripeSubId, periodEnd), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Handle_KnownSubscription_UpdatesCurrentPeriodEnd()
    {
        string stripeSubId = "sub_xyz789";
        DateTime periodEnd = DateTime.UtcNow.AddMonths(1);
        await SeedSubscription(stripeSubId, SubscriptionStatus.Trialing);

        await CreateSut().Handle(new HandleInvoicePaidCommand(stripeSubId, periodEnd), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .CurrentPeriodEnd.Should().Be(periodEnd);
    }

    [Fact]
    public async Task Handle_UnknownSubscription_DoesNotThrow()
    {
        Func<Task> act = () => CreateSut().Handle(
            new HandleInvoicePaidCommand("sub_unknown", DateTime.UtcNow.AddMonths(1)), default);

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
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(14),
            GracePeriodEnd = DateTime.UtcNow.AddDays(21)
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
