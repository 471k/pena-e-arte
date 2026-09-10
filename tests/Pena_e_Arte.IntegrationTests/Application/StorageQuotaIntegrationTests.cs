using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// Exercises the real StorageReconciliationJob -> Studio.StorageUsageBytes ->
/// PlanLimitService -> GetPresignedGuestUploadUrlHandler pipeline end to end (P1 #19).
/// IDistributedCache is a bare substitute (cache always misses, falls through to a real DB
/// read — PlanLimitService already handles a Redis-unavailable cache this way in production).
/// </summary>
[Collection("Database")]
public class StorageQuotaIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task StudioOverStorageQuota_AfterReconciliation_PresignThrowsPlanLimitExceeded()
    {
        Guid studioId = await SeedStudio();
        Guid planId = await SeedPlanWithStorageLimit(studioId, maxStorageGb: 1);

        IR2Service r2 = Substitute.For<IR2Service>();
        // 2 GiB of objects under this studio's prefix — over the 1 GB cap.
        r2.ListByPrefixAsync($"{studioId}/", Arg.Any<CancellationToken>())
          .Returns([new R2ObjectInfo($"{studioId}/huge.png", DateTime.UtcNow, 2L * 1024 * 1024 * 1024)]);

        await using AppDbContext reconcileDb = fixture.CreateDbContext(Guid.Empty);
        StorageReconciliationJob job = new(reconcileDb, r2, NullLogger<StorageReconciliationJob>.Instance);
        await job.RunAsync();

        Studio studio = await SeedGuestStudio(studioId);

        await using AppDbContext handlerDb = fixture.CreateDbContext(Guid.Empty);
        PlanLimitService planLimits = new(handlerDb, TenantFor(studioId), Substitute.For<IDistributedCache>(), NullLogger<PlanLimitService>.Instance);
        GetPresignedGuestUploadUrlHandler handler = new(handlerDb, r2, planLimits);

        Func<Task> act = () => handler.Handle(
            new GetPresignedGuestUploadUrlQuery(studio.Slug, new PresignGuestUploadRequest("image/png", "area")),
            default);

        await act.Should().ThrowAsync<PlanLimitExceededException>();
        _ = planId;
    }

    [Fact]
    public async Task StudioUnderStorageQuota_AfterReconciliation_PresignSucceeds()
    {
        Guid studioId = await SeedStudio();
        await SeedPlanWithStorageLimit(studioId, maxStorageGb: 10);

        IR2Service r2 = Substitute.For<IR2Service>();
        r2.ListByPrefixAsync($"{studioId}/", Arg.Any<CancellationToken>())
          .Returns([new R2ObjectInfo($"{studioId}/small.png", DateTime.UtcNow, 1024)]);
        r2.GeneratePresignedUploadUrlAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(("upload-url", "public-url"));

        await using AppDbContext reconcileDb = fixture.CreateDbContext(Guid.Empty);
        StorageReconciliationJob job = new(reconcileDb, r2, NullLogger<StorageReconciliationJob>.Instance);
        await job.RunAsync();

        Studio studio = await SeedGuestStudio(studioId);

        await using AppDbContext handlerDb = fixture.CreateDbContext(Guid.Empty);
        PlanLimitService planLimits = new(handlerDb, TenantFor(studioId), Substitute.For<IDistributedCache>(), NullLogger<PlanLimitService>.Instance);
        GetPresignedGuestUploadUrlHandler handler = new(handlerDb, r2, planLimits);

        PresignUploadResponse result = await handler.Handle(
            new GetPresignedGuestUploadUrlQuery(studio.Slug, new PresignGuestUploadRequest("image/png", "area")),
            default);

        result.UploadUrl.Should().Be("upload-url");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<Guid> SeedStudio()
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        Studio studio = new()
        {
            Name = "Storage Quota Test Studio",
            Slug = ("sq-" + Guid.NewGuid().ToString("N"))[..20],
            City = "Porto",
            OwnerEmail = $"sq{Guid.NewGuid():N}@test.com",
            IsActive = true,
            IsPublished = true,
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
        };
        db.Studios.Add(studio);
        await db.SaveChangesAsync();
        return studio.Id;
    }

    /// <summary>Re-fetches the seeded studio (for its slug) after the reconciliation job's
    /// SaveChangesAsync may have touched the row via a different context instance.</summary>
    private async Task<Studio> SeedGuestStudio(Guid studioId)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        return (await db.Studios.FindAsync(studioId))!;
    }

    private async Task<Guid> SeedPlanWithStorageLimit(Guid studioId, int maxStorageGb)
    {
        await using AppDbContext db = fixture.CreateDbContext(studioId);
        Plan plan = new() { Name = "Storage Test Plan", MaxStorageGb = maxStorageGb };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 29m });
        db.Plans.Add(plan);
        db.Subscriptions.Add(new Subscription { StudioId = studioId, PlanId = plan.Id });
        await db.SaveChangesAsync();
        return plan.Id;
    }

    private static ICurrentTenant TenantFor(Guid studioId)
    {
        CurrentTenantService t = new();
        t.SetTenant(studioId);
        return t;
    }
}
