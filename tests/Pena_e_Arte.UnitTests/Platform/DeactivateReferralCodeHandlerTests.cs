using FluentAssertions;
using Pena_e_Arte.Application.Platform.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

public class DeactivateReferralCodeHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private DeactivateReferralCodeHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ActiveCode_DeactivatesIt()
    {
        Guid codeId   = await SeedReferralCode(isActive: true);

        await CreateSut().Handle(new DeactivateReferralCodeCommand(codeId), default);

        _db.ReferralCodes.Single(r => r.Id == codeId).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_AlreadyInactiveCode_DeactivatesWithoutError()
    {
        Guid codeId = await SeedReferralCode(isActive: false);

        Func<Task> act = () => CreateSut().Handle(new DeactivateReferralCodeCommand(codeId), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_NonExistentCode_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new DeactivateReferralCodeCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<Guid> SeedReferralCode(bool isActive)
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Domain.Entities.Studio
        {
            Id         = studioId,
            Name       = "Test Studio",
            Slug       = Guid.NewGuid().ToString("N")[..20],
            City       = "Porto",
            OwnerEmail = $"{Guid.NewGuid():N}@test.com",
            IsActive   = true,
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
