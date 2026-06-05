using FluentAssertions;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class HandleSubscriptionUpdatedHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private HandleSubscriptionUpdatedHandler CreateSut() => new(_db);

    [Theory]
    [InlineData("active",   SubscriptionStatus.Active)]
    [InlineData("past_due", SubscriptionStatus.PastDue)]
    [InlineData("trialing", SubscriptionStatus.Trialing)]
    [InlineData("canceled", SubscriptionStatus.Cancelled)]
    public async Task Handle_KnownStripeStatus_MapsToExpectedStatus(
        string stripeStatus, SubscriptionStatus expected)
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Trialing);

        await CreateSut().Handle(
            new HandleSubscriptionUpdatedCommand(stripeSubId, stripeStatus), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .Status.Should().Be(expected);
    }

    [Fact]
    public async Task Handle_UnknownStripeStatus_DoesNotChangeStatus()
    {
        string stripeSubId = "sub_abc";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Active);

        await CreateSut().Handle(
            new HandleSubscriptionUpdatedCommand(stripeSubId, "paused"), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Handle_UnknownSubscription_DoesNotThrow()
    {
        Func<Task> act = () => CreateSut().Handle(
            new HandleSubscriptionUpdatedCommand("sub_unknown", "active"), default);

        await act.Should().NotThrowAsync();
    }

    private async Task SeedSubscription(string stripeSubId, SubscriptionStatus status)
    {
        _db.Subscriptions.Add(new Subscription
        {
            StudioId             = Guid.NewGuid(),
            StripeSubscriptionId = stripeSubId,
            Status               = status,
            TrialExpiresAt       = DateTime.UtcNow.AddDays(14),
            CurrentPeriodEnd     = DateTime.UtcNow.AddDays(14),
            GracePeriodEnd       = DateTime.UtcNow.AddDays(21)
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
