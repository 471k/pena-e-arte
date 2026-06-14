using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class CancelPlanChangeHandlerTests
{
    private readonly FakeDbContext         _db       = FakeDbContext.Create();
    private readonly ICurrentTenant        _tenant   = Substitute.For<ICurrentTenant>();
    private readonly IStripeBillingService _billing  = Substitute.For<IStripeBillingService>();
    private readonly Guid                  _studioId = Guid.NewGuid();

    public CancelPlanChangeHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private CancelPlanChangeHandler CreateSut() =>
        new(_db, _tenant, _billing, NullLogger<CancelPlanChangeHandler>.Instance);

    [Fact]
    public async Task Handle_PendingChange_ClearsPendingPlanAndReleasesSchedule()
    {
        await SeedSubscription(pendingPlanId: Guid.NewGuid(), stripeSubId: "sub_123");

        SubscriptionResponse result = await CreateSut().Handle(new CancelPlanChangeCommand(), default);

        result.PendingPlanId.Should().BeNull();
        _db.Subscriptions.Single(s => s.StudioId == _studioId).PendingPlanId.Should().BeNull();
        await _billing.Received(1).CancelScheduledPriceChangeAsync("sub_123", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoPendingChange_ThrowsBusinessRuleViolation()
    {
        await SeedSubscription(pendingPlanId: null, stripeSubId: "sub_123");

        Func<Task> act = () => CreateSut().Handle(new CancelPlanChangeCommand(), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*no pending plan change*");
    }

    [Fact]
    public async Task Handle_NoSubscription_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new CancelPlanChangeCommand(), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task SeedSubscription(Guid? pendingPlanId, string? stripeSubId)
    {
        _db.Subscriptions.Add(new Subscription
        {
            StudioId             = _studioId,
            PendingPlanId        = pendingPlanId,
            Status               = SubscriptionStatus.Active,
            StripeSubscriptionId = stripeSubId,
            TrialExpiresAt       = DateTime.UtcNow.AddDays(-20),
            CurrentPeriodEnd     = DateTime.UtcNow.AddDays(10),
            GracePeriodEnd       = DateTime.UtcNow.AddDays(-13),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
