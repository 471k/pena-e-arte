using FluentAssertions;
using Pena_e_Arte.Application.Plans.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class DeletePlanHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private DeletePlanHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_PlanWithNoSubscriptions_DeletesPlan()
    {
        Guid planId = await SeedPlan();

        await CreateSut().Handle(new DeletePlanCommand(planId), default);

        _db.Plans.Should().NotContain(p => p.Id == planId);
    }

    [Fact]
    public async Task Handle_PlanWithActiveSubscription_ThrowsBusinessRuleViolationException()
    {
        Guid planId = await SeedPlanWithSubscription();

        Func<Task> act = () => CreateSut().Handle(new DeletePlanCommand(planId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*active subscriptions*");
    }

    [Fact]
    public async Task Handle_NonExistentPlan_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new DeletePlanCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<Guid> SeedPlan()
    {
        Plan plan = new() { Name = "Pro" };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 49m });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return plan.Id;
    }

    private async Task<Guid> SeedPlanWithSubscription()
    {
        Guid studioId = Guid.NewGuid();
        Plan plan = new() { Name = "Pro" };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 49m });
        _db.Plans.Add(plan);

        Studio studio = new()
        {
            Id        = studioId,
            Name      = "Test Studio",
            Slug      = "test-studio",
            OwnerEmail = "owner@test.com"
        };
        _db.Studios.Add(studio);

        _db.Subscriptions.Add(new Subscription
        {
            StudioId         = studioId,
            PlanId           = plan.Id,
            Status           = SubscriptionStatus.Active,
            TrialExpiresAt   = DateTime.UtcNow.AddDays(14),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
            GracePeriodEnd   = DateTime.UtcNow.AddDays(21)
        });

        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return plan.Id;
    }
}
