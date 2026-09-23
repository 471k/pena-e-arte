using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class SuspendUnsuspendStudioHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ISubscriptionAccessService _access = Substitute.For<ISubscriptionAccessService>();
    private readonly IStripeBillingService _stripe = Substitute.For<IStripeBillingService>();

    private SuspendStudioHandler CreateSuspendSut() =>
        new(_db, _access, _stripe, NullLogger<SuspendStudioHandler>.Instance);

    private UnsuspendStudioHandler CreateUnsuspendSut() =>
        new(_db, _access, _stripe, NullLogger<UnsuspendStudioHandler>.Instance);

    private async Task<Studio> SeedStudioAsync(
        bool isActive, string? stripeSubscriptionId, SubscriptionStatus status = SubscriptionStatus.Active)
    {
        Studio studio = new()
        {
            Name = $"Studio-{Guid.NewGuid():N}"[..20],
            Slug = Guid.NewGuid().ToString("N")[..20],
            City = "Porto",
            OwnerEmail = $"{Guid.NewGuid():N}@test.com",
            IsActive = isActive,
            TrialExpiresAt = DateTime.UtcNow.AddDays(-30),
        };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();

        _db.Subscriptions.Add(new Subscription
        {
            StudioId = studio.Id,
            Status = status,
            StripeSubscriptionId = stripeSubscriptionId,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(10),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        return studio;
    }

    [Fact]
    public async Task Suspend_CardBilledActiveStudio_PausesStripeCollection()
    {
        Studio studio = await SeedStudioAsync(isActive: true, stripeSubscriptionId: "sub_123");

        await CreateSuspendSut().Handle(new SuspendStudioCommand(studio.Id), default);

        _db.Studios.Single(s => s.Id == studio.Id).IsActive.Should().BeFalse();
        await _stripe.Received(1).PauseCollectionAsync("sub_123", Arg.Any<CancellationToken>());
        await _access.Received(1).InvalidateCacheAsync(studio.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unsuspend_CardBilledStudio_ResumesStripeCollection()
    {
        Studio studio = await SeedStudioAsync(isActive: false, stripeSubscriptionId: "sub_123");

        await CreateUnsuspendSut().Handle(new UnsuspendStudioCommand(studio.Id), default);

        _db.Studios.Single(s => s.Id == studio.Id).IsActive.Should().BeTrue();
        await _stripe.Received(1).ResumeCollectionAsync("sub_123", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suspend_CashBilledStudio_MakesNoStripeCall()
    {
        Studio studio = await SeedStudioAsync(isActive: true, stripeSubscriptionId: null);

        await CreateSuspendSut().Handle(new SuspendStudioCommand(studio.Id), default);

        await _stripe.DidNotReceiveWithAnyArgs().PauseCollectionAsync(default!, default);
    }

    [Fact]
    public async Task Suspend_CancelledSubscription_MakesNoStripeCall()
    {
        Studio studio = await SeedStudioAsync(
            isActive: true, stripeSubscriptionId: "sub_123", status: SubscriptionStatus.Cancelled);

        await CreateSuspendSut().Handle(new SuspendStudioCommand(studio.Id), default);

        await _stripe.DidNotReceiveWithAnyArgs().PauseCollectionAsync(default!, default);
    }

    [Fact]
    public async Task Suspend_PauseThrows_StudioStillSuspended_NoExceptionToCaller()
    {
        Studio studio = await SeedStudioAsync(isActive: true, stripeSubscriptionId: "sub_123");
        _stripe.PauseCollectionAsync("sub_123", Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("Stripe unavailable"));

        Func<Task> act = () => CreateSuspendSut().Handle(new SuspendStudioCommand(studio.Id), default);

        await act.Should().NotThrowAsync();
        _db.Studios.Single(s => s.Id == studio.Id).IsActive.Should().BeFalse();
    }
}
