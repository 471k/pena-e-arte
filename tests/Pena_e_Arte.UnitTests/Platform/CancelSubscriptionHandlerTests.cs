using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Application.Platform.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

public class CancelSubscriptionHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IStripeBillingService _stripe = Substitute.For<IStripeBillingService>();

    private CancelSubscriptionHandler CreateSut() =>
        new(_db, _stripe, NullLogger<CancelSubscriptionHandler>.Instance);

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Trialing)]
    [InlineData(SubscriptionStatus.GracePeriod)]
    public async Task Handle_CancellableStatus_SetsStatusToCancelled(SubscriptionStatus status)
    {
        Guid studioId = await SeedStudio(status, stripeId: null);

        await CreateSut().Handle(new CancelSubscriptionCommand(studioId), default);

        _db.Subscriptions.Single(s => s.StudioId == studioId)
            .Status.Should().Be(SubscriptionStatus.Cancelled);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.PastDue)]
    public async Task Handle_WithStripeSubscriptionId_CallsStripeCancellation(SubscriptionStatus status)
    {
        const string stripeId = "sub_test_123";
        Guid studioId = await SeedStudio(status, stripeId: stripeId);

        await CreateSut().Handle(new CancelSubscriptionCommand(studioId), default);

        await _stripe.Received(1).CancelSubscriptionAsync(stripeId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithoutStripeSubscriptionId_DoesNotCallStripe()
    {
        Guid studioId = await SeedStudio(SubscriptionStatus.Active, stripeId: null);

        await CreateSut().Handle(new CancelSubscriptionCommand(studioId), default);

        await _stripe.DidNotReceive().CancelSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StripeCallFails_DoesNotThrow_AndLocalRecordIsStillCancelled()
    {
        const string stripeId = "sub_fail_456";
        Guid studioId = await SeedStudio(SubscriptionStatus.Active, stripeId: stripeId);

        _stripe.CancelSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .ThrowsAsync(new Exception("Stripe timeout"));

        Func<Task> act = () => CreateSut().Handle(new CancelSubscriptionCommand(studioId), default);

        await act.Should().NotThrowAsync();
        _db.Subscriptions.Single(s => s.StudioId == studioId)
            .Status.Should().Be(SubscriptionStatus.Cancelled);
    }

    [Fact]
    public async Task Handle_ClearsPendingPlanId()
    {
        Guid planId = Guid.NewGuid();
        Guid studioId = await SeedStudio(SubscriptionStatus.Active, stripeId: null, pendingPlanId: planId);

        await CreateSut().Handle(new CancelSubscriptionCommand(studioId), default);

        _db.Subscriptions.Single(s => s.StudioId == studioId)
            .PendingPlanId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_StudioNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new CancelSubscriptionCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_StudioWithNoSubscription_ThrowsBusinessRuleViolation()
    {
        _db.Studios.Add(new Studio
        {
            Id = Guid.NewGuid(),
            Name = "No-Sub Studio",
            Slug = "no-sub-cancel",
            TrialExpiresAt = DateTime.UtcNow.AddDays(7),
        });
        await _db.SaveChangesAsync();
        Guid studioId = _db.Studios.First(s => s.Slug == "no-sub-cancel").Id;
        _db.ChangeTracker.Clear();

        Func<Task> act = () => CreateSut().Handle(new CancelSubscriptionCommand(studioId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Theory]
    [InlineData(SubscriptionStatus.Cancelled)]
    public async Task Handle_AlreadyCancelledStatus_ThrowsBusinessRuleViolation(SubscriptionStatus status)
    {
        Guid studioId = await SeedStudio(status, stripeId: null);

        Func<Task> act = () => CreateSut().Handle(new CancelSubscriptionCommand(studioId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    private async Task<Guid> SeedStudio(
        SubscriptionStatus status,
        string? stripeId,
        Guid? pendingPlanId = null)
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio
        {
            Id = studioId,
            Name = $"Studio-{studioId:N}"[..20],
            Slug = studioId.ToString("N")[..20],
            TrialExpiresAt = DateTime.UtcNow.AddDays(7),
        });
        _db.Subscriptions.Add(new Subscription
        {
            StudioId = studioId,
            Status = status,
            TrialExpiresAt = DateTime.UtcNow.AddDays(7),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
            StripeSubscriptionId = stripeId,
            PendingPlanId = pendingPlanId,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return studioId;
    }
}
