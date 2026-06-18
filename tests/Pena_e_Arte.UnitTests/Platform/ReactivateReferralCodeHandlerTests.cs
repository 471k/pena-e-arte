using FluentAssertions;
using Pena_e_Arte.Application.Platform.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

public class ReactivateReferralCodeHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private ReactivateReferralCodeHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ValidCode_SetsIsActiveTrue()
    {
        Guid codeId = await SeedReferralCode(isActive: false);

        await CreateSut().Handle(new ReactivateReferralCodeCommand(codeId), default);

        _db.ReferralCodes.Single(r => r.Id == codeId).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_DeactivatesOtherActiveCodes()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio
        {
            Id             = studioId,
            Name           = "Test Studio",
            Slug           = Guid.NewGuid().ToString("N")[..20],
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
        });

        ReferralCode targetCode = new() { StudioId = studioId, Code = "TARGET01", IsActive = false };
        ReferralCode activeCode = new() { StudioId = studioId, Code = "ACTIVE01", IsActive = true };
        _db.ReferralCodes.Add(targetCode);
        _db.ReferralCodes.Add(activeCode);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateSut().Handle(new ReactivateReferralCodeCommand(targetCode.Id), default);

        _db.ReferralCodes.Single(r => r.Id == targetCode.Id).IsActive.Should().BeTrue();
        _db.ReferralCodes.Single(r => r.Id == activeCode.Id).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_CodeNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () =>
            CreateSut().Handle(new ReactivateReferralCodeCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<Guid> SeedReferralCode(bool isActive)
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio
        {
            Id             = studioId,
            Name           = "Test Studio",
            Slug           = Guid.NewGuid().ToString("N")[..20],
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
        });
        ReferralCode code = new()
        {
            StudioId = studioId,
            Code     = Guid.NewGuid().ToString("N")[..8].ToUpper(),
            IsActive = isActive,
        };
        _db.ReferralCodes.Add(code);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return code.Id;
    }
}
