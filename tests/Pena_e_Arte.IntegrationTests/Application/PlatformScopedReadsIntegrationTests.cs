using FluentAssertions;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// D08 on real MySQL: the admin studio detail page reads ONE studio's subscription and referral codes
/// through the same approved cross-tenant reads (usages #5 and #6), instead of downloading every studio's.
/// </summary>
[Collection("Database")]
public class PlatformScopedReadsIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Subscriptions_ScopedToOneStudio_ReturnOnlyThatStudio()
    {
        Guid target = await SeedStudio(withCode: null);
        Guid other = await SeedStudio(withCode: null);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GetPlatformSubscriptionsHandler handler = new(db);

        List<PlatformSubscriptionResponse> scoped = await handler.Handle(new GetPlatformSubscriptionsQuery(target), default);
        List<PlatformSubscriptionResponse> all = await handler.Handle(new GetPlatformSubscriptionsQuery(), default);

        scoped.Should().ContainSingle().Which.StudioId.Should().Be(target);
        all.Select(s => s.StudioId).Should().Contain([target, other]); // unscoped behaviour unchanged
    }

    [Fact]
    public async Task ReferralCodes_ScopedToOneStudio_ReturnOnlyItsCodes()
    {
        string targetCode = $"S{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        string otherCode = $"S{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        Guid target = await SeedStudio(targetCode);
        Guid other = await SeedStudio(otherCode);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GetPlatformReferralCodesHandler handler = new(db);

        List<PlatformReferralCodeResponse> scoped = await handler.Handle(new GetPlatformReferralCodesQuery(target), default);
        List<PlatformReferralCodeResponse> all = await handler.Handle(new GetPlatformReferralCodesQuery(), default);

        scoped.Should().ContainSingle().Which.Code.Should().Be(targetCode);
        all.Select(c => c.StudioId).Should().Contain([target, other]);
    }

    private async Task<Guid> SeedStudio(string? withCode)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        Studio studio = new()
        {
            Name = $"Scoped Studio {Guid.NewGuid():N}"[..28],
            Slug = Guid.NewGuid().ToString("N")[..20],
            City = "Tirana",
            OwnerEmail = $"{Guid.NewGuid():N}@test.com",
            IsActive = true,
            TrialExpiresAt = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
        };
        db.Studios.Add(studio);
        await db.SaveChangesAsync();
        if (withCode is not null)
        {
            db.ReferralCodes.Add(new ReferralCode { StudioId = studio.Id, Code = withCode, IsActive = true });
            await db.SaveChangesAsync();
        }
        return studio.Id;
    }
}
