using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Jobs;

public class StorageReconciliationJobTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IR2Service _r2 = Substitute.For<IR2Service>();

    private StorageReconciliationJob CreateSut() =>
        new(_db, _r2, NullLogger<StorageReconciliationJob>.Instance);

    private Studio SeedStudio(bool isActive = true)
    {
        Studio studio = new()
        {
            Name = "Test", Slug = $"test-{Guid.NewGuid():N}", City = "Porto", OwnerEmail = "x@x.com",
            IsActive = isActive, TrialExpiresAt = DateTime.UtcNow.AddDays(14),
        };
        _db.Studios.Add(studio);
        _db.SaveChanges();
        return studio;
    }

    [Fact]
    public async Task RunAsync_SumsObjectSizesPerStudio()
    {
        Studio studio = SeedStudio();
        _r2.ListByPrefixAsync($"{studio.Id}/", Arg.Any<CancellationToken>()).Returns(
        [
            new R2ObjectInfo($"{studio.Id}/a.png", DateTime.UtcNow, 1000),
            new R2ObjectInfo($"{studio.Id}/b.png", DateTime.UtcNow, 2500),
        ]);

        await CreateSut().RunAsync();

        _db.Studios.Single(s => s.Id == studio.Id).StorageUsageBytes.Should().Be(3500);
    }

    [Fact]
    public async Task RunAsync_StudioWithZeroObjects_WritesZeroNotSkip()
    {
        Studio studio = SeedStudio();
        studio.StorageUsageBytes = 999; // stale value from a previous run
        await _db.SaveChangesAsync();
        _r2.ListByPrefixAsync($"{studio.Id}/", Arg.Any<CancellationToken>()).Returns([]);

        await CreateSut().RunAsync();

        _db.Studios.Single(s => s.Id == studio.Id).StorageUsageBytes.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_DoesNotCrossContaminateBetweenStudios()
    {
        Studio studioA = SeedStudio();
        Studio studioB = SeedStudio();
        _r2.ListByPrefixAsync($"{studioA.Id}/", Arg.Any<CancellationToken>()).Returns(
            [new R2ObjectInfo($"{studioA.Id}/a.png", DateTime.UtcNow, 500)]);
        _r2.ListByPrefixAsync($"{studioB.Id}/", Arg.Any<CancellationToken>()).Returns(
            [new R2ObjectInfo($"{studioB.Id}/b.png", DateTime.UtcNow, 7000)]);

        await CreateSut().RunAsync();

        _db.Studios.Single(s => s.Id == studioA.Id).StorageUsageBytes.Should().Be(500);
        _db.Studios.Single(s => s.Id == studioB.Id).StorageUsageBytes.Should().Be(7000);
    }

    [Fact]
    public async Task RunAsync_InactiveStudio_IsSkipped()
    {
        Studio inactive = SeedStudio(isActive: false);

        await CreateSut().RunAsync();

        await _r2.DidNotReceive().ListByPrefixAsync($"{inactive.Id}/", Arg.Any<CancellationToken>());
    }
}
