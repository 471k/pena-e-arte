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

    // ── Revenue ledger ────────────────────────────────────────────────────

    private async Task SetBilled(Guid studioId, decimal amount)
    {
        Subscription sub = _db.Subscriptions.Single(s => s.StudioId == studioId);
        sub.BilledUnitAmount = amount;
        sub.BilledQuantity = 1;
        sub.BilledCurrency = "eur";
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private List<RevenueEventType> LedgerTypesInOrder() =>
        _db.SubscriptionRevenueEvents.OrderBy(e => e.OccurredAt).ThenBy(e => e.CreatedAt)
            .Select(e => e.Type).ToList();

    [Fact]
    public async Task Suspend_ActiveBillingStudio_WritesPausedWithUnchangedMrr()
    {
        Studio studio = await SeedStudioAsync(isActive: true, stripeSubscriptionId: null);
        await SetBilled(studio.Id, 59m);

        await CreateSuspendSut().Handle(new SuspendStudioCommand(studio.Id), default);

        SubscriptionRevenueEvent ledgerEvent = _db.SubscriptionRevenueEvents.Single();
        ledgerEvent.Type.Should().Be(RevenueEventType.Paused);
        ledgerEvent.MrrBefore.Should().Be(59m);
        ledgerEvent.MrrAfter.Should().Be(59m);
    }

    [Fact]
    public async Task SuspendThenUnsuspend_WritesPausedThenResumed()
    {
        Studio studio = await SeedStudioAsync(isActive: true, stripeSubscriptionId: null);
        await SetBilled(studio.Id, 59m);

        await CreateSuspendSut().Handle(new SuspendStudioCommand(studio.Id), default);
        _db.ChangeTracker.Clear();
        await CreateUnsuspendSut().Handle(new UnsuspendStudioCommand(studio.Id), default);

        LedgerTypesInOrder().Should().Equal(RevenueEventType.Paused, RevenueEventType.Resumed);
    }

    [Fact]
    public async Task Suspend_AlreadyPaused_WritesNoSecondPausedEvent()
    {
        Studio studio = await SeedStudioAsync(isActive: true, stripeSubscriptionId: null);
        await SetBilled(studio.Id, 59m);

        await CreateSuspendSut().Handle(new SuspendStudioCommand(studio.Id), default);
        _db.ChangeTracker.Clear();
        await CreateSuspendSut().Handle(new SuspendStudioCommand(studio.Id), default);

        _db.SubscriptionRevenueEvents.Count().Should().Be(1);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Trialing)]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Cancelled)]
    public async Task Suspend_SubscriptionNotActivelyBilling_WritesNoEvent(SubscriptionStatus status)
    {
        Studio studio = await SeedStudioAsync(isActive: true, stripeSubscriptionId: null, status);
        await SetBilled(studio.Id, 59m);

        await CreateSuspendSut().Handle(new SuspendStudioCommand(studio.Id), default);

        _db.SubscriptionRevenueEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Suspend_FreePlanZeroMrr_WritesNoEvent()
    {
        Studio studio = await SeedStudioAsync(isActive: true, stripeSubscriptionId: null);
        await SetBilled(studio.Id, 0m);

        await CreateSuspendSut().Handle(new SuspendStudioCommand(studio.Id), default);

        _db.SubscriptionRevenueEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Unsuspend_NeverPaused_WritesNoResumedEvent()
    {
        Studio studio = await SeedStudioAsync(isActive: false, stripeSubscriptionId: null);
        await SetBilled(studio.Id, 59m);

        await CreateUnsuspendSut().Handle(new UnsuspendStudioCommand(studio.Id), default);

        _db.SubscriptionRevenueEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Unsuspend_SubscriptionCancelledWhileSuspended_DoesNotResurrectIt()
    {
        Studio studio = await SeedStudioAsync(isActive: true, stripeSubscriptionId: null);
        await SetBilled(studio.Id, 59m);
        await CreateSuspendSut().Handle(new SuspendStudioCommand(studio.Id), default);
        _db.ChangeTracker.Clear();

        Subscription sub = _db.Subscriptions.Single(s => s.StudioId == studio.Id);
        sub.Status = SubscriptionStatus.Cancelled;
        _db.SubscriptionRevenueEvents.Add(new SubscriptionRevenueEvent
        {
            SubscriptionId = sub.Id,
            StudioId = studio.Id,
            OccurredAt = DateTime.UtcNow.AddSeconds(1),
            Type = RevenueEventType.Churn,
            MrrBefore = 59m,
            MrrAfter = 0m,
            Source = "seed",
            StripeEventId = "seed-churn",
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateUnsuspendSut().Handle(new UnsuspendStudioCommand(studio.Id), default);

        LedgerTypesInOrder().Should().Equal(RevenueEventType.Paused, RevenueEventType.Churn);
    }
}
