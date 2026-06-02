using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Billing.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class GetSubscriptionHandlerTests
{
    private readonly FakeDbContext  _db       = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant   = Substitute.For<ICurrentTenant>();
    private readonly Guid           _studioId = Guid.NewGuid();

    public GetSubscriptionHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private GetSubscriptionHandler CreateSut() => new(_db, _tenant);

    [Fact]
    public async Task Handle_ExistingSubscription_ReturnsSubscriptionResponse()
    {
        Guid subId = await SeedSubscription(SubscriptionStatus.Trialing);

        SubscriptionResponse result = await CreateSut().Handle(new GetSubscriptionQuery(), default);

        result.Id.Should().Be(subId);
        result.StudioId.Should().Be(_studioId);
        result.Status.Should().Be(SubscriptionStatus.Trialing.ToString());
    }

    [Fact]
    public async Task Handle_NoSubscriptionForTenant_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetSubscriptionQuery(), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_SubscriptionFromDifferentTenant_ThrowsNotFoundException()
    {
        _db.Subscriptions.Add(new Subscription
        {
            StudioId         = Guid.NewGuid(),
            Status           = SubscriptionStatus.Active,
            TrialExpiresAt   = DateTime.UtcNow.AddDays(14),
            GracePeriodEnd   = DateTime.UtcNow.AddDays(21),
            CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1)
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(new GetSubscriptionQuery(), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<Guid> SeedSubscription(SubscriptionStatus status)
    {
        Subscription sub = new()
        {
            StudioId         = _studioId,
            Status           = status,
            TrialExpiresAt   = DateTime.UtcNow.AddDays(14),
            GracePeriodEnd   = DateTime.UtcNow.AddDays(21),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(14)
        };
        _db.Subscriptions.Add(sub);
        await _db.SaveChangesAsync();
        return sub.Id;
    }
}
