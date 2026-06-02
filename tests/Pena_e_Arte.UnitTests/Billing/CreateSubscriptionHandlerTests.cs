using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class CreateSubscriptionHandlerTests
{
    private readonly FakeDbContext  _db       = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant   = Substitute.For<ICurrentTenant>();
    private readonly Guid           _studioId = Guid.NewGuid();

    public CreateSubscriptionHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private CreateSubscriptionHandler CreateSut() => new(_db, _tenant);

    [Fact]
    public async Task Handle_ValidPlanAndTrialingSubscription_ReturnsActiveSubscription()
    {
        Guid planId = await SeedPlan();
        await SeedSubscription(SubscriptionStatus.Trialing);

        SubscriptionResponse result = await CreateSut()
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId)), default);

        result.Status.Should().Be(SubscriptionStatus.Active.ToString());
        result.PlanId.Should().Be(planId);
    }

    [Fact]
    public async Task Handle_ValidPlanAndTrialingSubscription_PersistsActiveStatus()
    {
        Guid planId = await SeedPlan();
        await SeedSubscription(SubscriptionStatus.Trialing);

        await CreateSut()
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId)), default);

        _db.Subscriptions.Single(s => s.StudioId == _studioId).Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Handle_ValidPlanAndGracePeriodSubscription_ActivatesSubscription()
    {
        Guid planId = await SeedPlan();
        await SeedSubscription(SubscriptionStatus.GracePeriod);

        SubscriptionResponse result = await CreateSut()
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId)), default);

        result.Status.Should().Be(SubscriptionStatus.Active.ToString());
    }

    [Fact]
    public async Task Handle_PlanNotFound_ThrowsNotFoundException()
    {
        await SeedSubscription(SubscriptionStatus.Trialing);

        Func<Task> act = () => CreateSut()
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(Guid.NewGuid())), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_SubscriptionNotFound_ThrowsNotFoundException()
    {
        Guid planId = await SeedPlan();

        Func<Task> act = () => CreateSut()
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId)), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_AlreadyActiveSubscription_ThrowsBusinessRuleViolationException()
    {
        Guid planId = await SeedPlan();
        await SeedSubscription(SubscriptionStatus.Active);

        Func<Task> act = () => CreateSut()
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*active*");
    }

    private async Task<Guid> SeedPlan()
    {
        Plan plan = new() { Name = "Pro", BillingInterval = BillingInterval.Monthly, PriceMonthly = 49m };
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        return plan.Id;
    }

    private async Task SeedSubscription(SubscriptionStatus status)
    {
        _db.Subscriptions.Add(new Subscription
        {
            StudioId         = _studioId,
            Status           = status,
            TrialExpiresAt   = DateTime.UtcNow.AddDays(14),
            GracePeriodEnd   = DateTime.UtcNow.AddDays(21),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(14)
        });
        await _db.SaveChangesAsync();
    }
}
