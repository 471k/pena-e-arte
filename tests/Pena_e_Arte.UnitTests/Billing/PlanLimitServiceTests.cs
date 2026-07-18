using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class PlanLimitServiceTests
{
    private readonly FakeDbContext  _db       = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant   = Substitute.For<ICurrentTenant>();
    private readonly IDistributedCache _cache = new MemoryDistributedCache(
        Options.Create(new MemoryDistributedCacheOptions()));
    private readonly Guid _studioId = Guid.NewGuid();

    public PlanLimitServiceTests() => _tenant.StudioId.Returns(_studioId);

    private PlanLimitService CreateSut() =>
        new(_db, _tenant, _cache, NullLogger<PlanLimitService>.Instance);

    private async Task<Guid> SeedPlanAndSubscription(Plan plan)
    {
        _db.Plans.Add(plan);
        _db.Studios.Add(new Studio
        {
            Id         = _studioId,
            Name       = "Test Studio",
            Slug       = "test-studio",
            OwnerEmail = "owner@test.com",
        });
        _db.Subscriptions.Add(new Subscription
        {
            StudioId         = _studioId,
            PlanId           = plan.Id,
            Status           = SubscriptionStatus.Active,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
            GracePeriodEnd   = DateTime.UtcNow.AddDays(37),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return plan.Id;
    }

    [Fact]
    public async Task EnsureWithinLimitAsync_NoSubscriptionForStudio_DoesNotThrow()
    {
        Func<Task> act = () => CreateSut().EnsureWithinLimitAsync(QuotaType.Artists, default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureWithinLimitAsync_UnlimitedPlan_DoesNotThrow()
    {
        await SeedPlanAndSubscription(new Plan { Name = "Pro", MaxArtists = null });

        for (int i = 0; i < 20; i++)
            _db.Artists.Add(new Artist { StudioId = _studioId, FirstName = "A", LastName = "B", Email = $"a{i}@x.com" });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().EnsureWithinLimitAsync(QuotaType.Artists, default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureWithinLimitAsync_UnderLimit_DoesNotThrow()
    {
        await SeedPlanAndSubscription(new Plan { Name = "Starter", MaxArtists = 2 });

        _db.Artists.Add(new Artist { StudioId = _studioId, FirstName = "A", LastName = "B", Email = "a@x.com" });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().EnsureWithinLimitAsync(QuotaType.Artists, default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureWithinLimitAsync_AtLimit_ThrowsPlanLimitExceededException()
    {
        await SeedPlanAndSubscription(new Plan { Name = "Starter", MaxArtists = 1 });

        _db.Artists.Add(new Artist { StudioId = _studioId, FirstName = "A", LastName = "B", Email = "a@x.com" });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().EnsureWithinLimitAsync(QuotaType.Artists, default);

        await act.Should().ThrowAsync<PlanLimitExceededException>()
            .WithMessage("*1 artists*");
    }

    [Fact]
    public async Task EnsureWithinLimitAsync_AppointmentsPerMonth_OnlyCountsCurrentMonth()
    {
        await SeedPlanAndSubscription(new Plan { Name = "Starter", MaxAppointmentsPerMonth = 1 });

        // One from last month (should not count) + none this month yet — under limit.
        _db.Appointments.Add(new Appointment
        {
            StudioId        = _studioId,
            ArtistId        = Guid.NewGuid(),
            ClientId        = Guid.NewGuid(),
            Date            = DateTime.UtcNow,
            EndDate         = DateTime.UtcNow.AddHours(1),
            DurationMinutes = 60,
            Status          = AppointmentStatus.Pending,
            DepositStatus   = DepositStatus.Pending,
            CreatedAt       = DateTime.UtcNow.AddMonths(-1),
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().EnsureWithinLimitAsync(QuotaType.AppointmentsPerMonth, default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureWithinLimitAsync_AppointmentsThisMonthAtLimit_Throws()
    {
        await SeedPlanAndSubscription(new Plan { Name = "Starter", MaxAppointmentsPerMonth = 1 });

        _db.Appointments.Add(new Appointment
        {
            StudioId        = _studioId,
            ArtistId        = Guid.NewGuid(),
            ClientId        = Guid.NewGuid(),
            Date            = DateTime.UtcNow,
            EndDate         = DateTime.UtcNow.AddHours(1),
            DurationMinutes = 60,
            Status          = AppointmentStatus.Pending,
            DepositStatus   = DepositStatus.Pending,
            CreatedAt       = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().EnsureWithinLimitAsync(QuotaType.AppointmentsPerMonth, default);

        await act.Should().ThrowAsync<PlanLimitExceededException>();
    }

    [Fact]
    public async Task EnsureWithinLimitAsync_StorageAtLimit_Throws()
    {
        Guid planId = await SeedPlanAndSubscription(new Plan { Name = "Starter", MaxStorageGb = 1 });

        Studio studio = await _db.Studios.FindAsync(_studioId) ?? throw new InvalidOperationException();
        studio.StorageUsageBytes = 1L * 1024 * 1024 * 1024; // exactly 1 GB
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        Func<Task> act = () => CreateSut().EnsureWithinLimitAsync(QuotaType.StorageBytes, default);

        await act.Should().ThrowAsync<PlanLimitExceededException>();

        // Keep the compiler from complaining about the unused planId in case assertions change.
        planId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task EnsureWithinLimitAsync_UsageIsCached_DoesNotReflectChangesWithinTtl()
    {
        await SeedPlanAndSubscription(new Plan { Name = "Starter", MaxArtists = 2 });

        _db.Artists.Add(new Artist { StudioId = _studioId, FirstName = "A", LastName = "B", Email = "a@x.com" });
        await _db.SaveChangesAsync();

        PlanLimitService sut = CreateSut();

        // First check: 1 artist, under the limit of 2 — passes and caches "1".
        await sut.EnsureWithinLimitAsync(QuotaType.Artists, default);

        // A second artist is added directly (bypassing the handler that would normally
        // trigger this check) — usage is now 2, at the limit.
        _db.Artists.Add(new Artist { StudioId = _studioId, FirstName = "C", LastName = "D", Email = "c@x.com" });
        await _db.SaveChangesAsync();

        // Immediately re-checking still sees the cached count (1) since the 30s TTL
        // hasn't elapsed — demonstrates the caching trade-off documented on the service.
        Func<Task> act = () => sut.EnsureWithinLimitAsync(QuotaType.Artists, default);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureWithinLimitAsync_AfterInvalidateUsageCache_ReflectsFreshlyAddedEntities()
    {
        await SeedPlanAndSubscription(new Plan { Name = "Starter", MaxArtists = 2 });

        _db.Artists.Add(new Artist { StudioId = _studioId, FirstName = "A", LastName = "B", Email = "a@x.com" });
        await _db.SaveChangesAsync();

        PlanLimitService sut = CreateSut();

        // First check: 1 artist, under the limit of 2 — passes and caches "1".
        await sut.EnsureWithinLimitAsync(QuotaType.Artists, default);

        _db.Artists.Add(new Artist { StudioId = _studioId, FirstName = "C", LastName = "D", Email = "c@x.com" });
        await _db.SaveChangesAsync();

        // Mirror image of the caching test above: this time the cache is explicitly
        // invalidated between the two checks, so the second check must see the fresh
        // count (2) and throw, rather than reading the stale cached "1".
        await sut.InvalidateUsageCacheAsync(QuotaType.Artists, default);

        Func<Task> act = () => sut.EnsureWithinLimitAsync(QuotaType.Artists, default);
        await act.Should().ThrowAsync<PlanLimitExceededException>();
    }

    [Fact]
    public async Task GetUsageSnapshotAsync_NoSubscriptionForStudio_ReturnsNull()
    {
        PlanUsageSnapshot? result = await CreateSut().GetUsageSnapshotAsync(default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetUsageSnapshotAsync_PopulatedPlan_ReturnsCorrectCountsPerDimension()
    {
        await SeedPlanAndSubscription(new Plan
        {
            Name = "Starter", MaxArtists = 6, MaxAppointmentsPerMonth = 40,
            MaxNotificationsPerMonth = 150, MaxStorageGb = 2, MaxLocations = 1,
        });

        _db.Artists.Add(new Artist { StudioId = _studioId, FirstName = "A", LastName = "B", Email = "a@x.com" });
        _db.Artists.Add(new Artist { StudioId = _studioId, FirstName = "C", LastName = "D", Email = "c@x.com" });
        _db.Appointments.Add(new Appointment
        {
            StudioId = _studioId, ArtistId = Guid.NewGuid(), ClientId = Guid.NewGuid(),
            Date = DateTime.UtcNow, EndDate = DateTime.UtcNow.AddHours(1), DurationMinutes = 60,
            Status = AppointmentStatus.Pending, DepositStatus = DepositStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        PlanUsageSnapshot? result = await CreateSut().GetUsageSnapshotAsync(default);

        result.Should().NotBeNull();
        result!.PlanName.Should().Be("Starter");
        result.Artists.Current.Should().Be(2);
        result.Artists.Max.Should().Be(6);
        result.AppointmentsPerMonth.Current.Should().Be(1);
        result.AppointmentsPerMonth.Max.Should().Be(40);
        result.NotificationsPerMonth.Current.Should().Be(0);
        result.NotificationsPerMonth.Max.Should().Be(150);
        result.Locations.Current.Should().Be(1);
        result.Locations.Max.Should().Be(1);
    }

    [Fact]
    public async Task GetUsageSnapshotAsync_UnlimitedPlan_MaxIsNullForEveryDimension()
    {
        await SeedPlanAndSubscription(new Plan { Name = "Pro" });

        PlanUsageSnapshot? result = await CreateSut().GetUsageSnapshotAsync(default);

        result.Should().NotBeNull();
        result!.Artists.Max.Should().BeNull();
        result.AppointmentsPerMonth.Max.Should().BeNull();
        result.NotificationsPerMonth.Max.Should().BeNull();
        result.StorageGb.Max.Should().BeNull();
        result.Locations.Max.Should().BeNull();
    }

    [Fact]
    public async Task GetUsageSnapshotAsync_StorageBytes_ConvertsToGbRoundedToOneDecimal()
    {
        await SeedPlanAndSubscription(new Plan { Name = "Starter", MaxStorageGb = 10 });

        Studio studio = await _db.Studios.FindAsync(_studioId) ?? throw new InvalidOperationException();
        // 2.5 GB, expressed in bytes — should round-trip to exactly 2.5 (1 decimal).
        studio.StorageUsageBytes = (long)(2.5 * 1024 * 1024 * 1024);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        PlanUsageSnapshot? result = await CreateSut().GetUsageSnapshotAsync(default);

        result.Should().NotBeNull();
        result!.StorageGb.Current.Should().Be(2.5);
        result.StorageGb.Max.Should().Be(10);
    }
}
