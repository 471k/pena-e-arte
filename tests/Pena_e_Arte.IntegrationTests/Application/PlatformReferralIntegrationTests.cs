using FluentAssertions;
using Pena_e_Arte.Application.Platform.Commands;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class PlatformReferralIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task GetPlatformReferralCodes_ReturnsCrossStudioCodes()
    {
        Guid studioId = await SeedStudioWithCode("TESTREF1");

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GetPlatformReferralCodesHandler handler = new(db);

        List<PlatformReferralCodeResponse> result =
            await handler.Handle(new GetPlatformReferralCodesQuery(), default);

        PlatformReferralCodeResponse? code = result.FirstOrDefault(r => r.StudioId == studioId);
        code.Should().NotBeNull();
        code!.Code.Should().Be("TESTREF1");
        code.ShareUrl.Should().Be("https://tattooos.co/register?ref=TESTREF1");
        code.IsActive.Should().BeTrue();
        code.RedemptionCount.Should().Be(0);
    }

    [Fact]
    public async Task DeactivateReferralCode_ExistingCode_SetsIsActiveFalse()
    {
        Guid codeId = await SeedCode("DEACREF1");

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        DeactivateReferralCodeHandler handler = new(db);

        await handler.Handle(new DeactivateReferralCodeCommand(codeId), default);

        await using AppDbContext verifyDb = fixture.CreateDbContext(Guid.Empty);
        ReferralCode? stored = await verifyDb.ReferralCodes.FindAsync(codeId);
        stored.Should().NotBeNull();
        stored!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeactivateReferralCode_NonExistentId_ThrowsNotFoundException()
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        DeactivateReferralCodeHandler handler = new(db);

        Func<Task> act = () => handler.Handle(
            new DeactivateReferralCodeCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<Guid> SeedStudioWithCode(string code)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        Studio studio = new()
        {
            Name = $"Ref Studio {Guid.NewGuid():N}"[..25],
            Slug = Guid.NewGuid().ToString("N")[..20],
            City = "Lisbon",
            OwnerEmail = $"{Guid.NewGuid():N}@test.com",
            IsActive = true,
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
        };
        db.Studios.Add(studio);
        await db.SaveChangesAsync();

        db.ReferralCodes.Add(new ReferralCode
        {
            StudioId = studio.Id,
            Code = code,
            IsActive = true,
        });
        await db.SaveChangesAsync();
        return studio.Id;
    }

    private async Task<Guid> SeedCode(string code)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        Studio studio = new()
        {
            Name = $"Ref Studio {Guid.NewGuid():N}"[..25],
            Slug = Guid.NewGuid().ToString("N")[..20],
            City = "Lisbon",
            OwnerEmail = $"{Guid.NewGuid():N}@test.com",
            IsActive = true,
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
        };
        db.Studios.Add(studio);
        await db.SaveChangesAsync();

        ReferralCode referralCode = new()
        {
            StudioId = studio.Id,
            Code = code,
            IsActive = true,
        };
        db.ReferralCodes.Add(referralCode);
        await db.SaveChangesAsync();
        return referralCode.Id;
    }
}
