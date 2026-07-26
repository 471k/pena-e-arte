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
}
