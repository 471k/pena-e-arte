using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Billing.Commands.CreateBillingPortal;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class CreateBillingPortalHandlerTests
{
    private readonly FakeDbContext        _db       = FakeDbContext.Create();
    private readonly ICurrentTenant       _tenant   = Substitute.For<ICurrentTenant>();
    private readonly IStripeBillingService _billing = Substitute.For<IStripeBillingService>();
    private readonly Guid                 _studioId = Guid.NewGuid();

    public CreateBillingPortalHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private CreateBillingPortalHandler CreateSut() =>
        new(_db, _tenant, _billing, NullLogger<CreateBillingPortalHandler>.Instance);

    [Fact]
    public async Task Handle_ReturnsPortalUrl_WhenSubscriptionHasStripeCustomerId()
    {
        const string customerId = "cus_test_123";
        const string returnUrl  = "https://app.penaearte.com/billing";
        const string portalUrl  = "https://billing.stripe.com/session/test_abc";

        await SeedSubscription(stripeCustomerId: customerId);
        _billing.CreatePortalSessionAsync(customerId, returnUrl, Arg.Any<CancellationToken>())
                .Returns(portalUrl);

        CreateBillingPortalResult result = await CreateSut().Handle(
            new CreateBillingPortalCommand(returnUrl), default);

        result.Url.Should().Be(portalUrl);
        await _billing.Received(1)
            .CreatePortalSessionAsync(customerId, returnUrl, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ThrowsNotFoundException_WhenSubscriptionIsNull()
    {
        Func<Task> act = () => CreateSut().Handle(
            new CreateBillingPortalCommand("https://app.example.com/billing"), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ThrowsNotFoundException_WhenStripeCustomerIdIsNull()
    {
        await SeedSubscription(stripeCustomerId: null);

        Func<Task> act = () => CreateSut().Handle(
            new CreateBillingPortalCommand("https://app.example.com/billing"), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task SeedSubscription(string? stripeCustomerId)
    {
        Studio studio = new()
        {
            Id               = _studioId,
            Name             = "Test Studio",
            Slug             = "test-studio",
            OwnerEmail       = "owner@test.com",
            StripeCustomerId = stripeCustomerId,
        };

        _db.Studios.Add(studio);

        _db.Subscriptions.Add(new Subscription
        {
            StudioId         = _studioId,
            Studio           = studio,
            Status           = SubscriptionStatus.Active,
            TrialExpiresAt   = DateTime.UtcNow.AddDays(-20),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(10),
            GracePeriodEnd   = DateTime.UtcNow.AddDays(-13),
        });

        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
