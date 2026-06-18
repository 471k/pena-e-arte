using FluentAssertions;
using Pena_e_Arte.Application.Platform.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

public class DeleteReferralCodeHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private DeleteReferralCodeHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_CodeWithNoRedemptions_DeletesSuccessfully()
    {
        Guid codeId = await SeedReferralCode(redemptionCount: 0);

        await CreateSut().Handle(new DeleteReferralCodeCommand(codeId), default);

        _db.ReferralCodes.Any(r => r.Id == codeId).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_CodeWithRedemptions_ThrowsBusinessRuleViolationException()
    {
        Guid codeId = await SeedReferralCode(redemptionCount: 1);

        Func<Task> act = () =>
            CreateSut().Handle(new DeleteReferralCodeCommand(codeId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_CodeNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () =>
            CreateSut().Handle(new DeleteReferralCodeCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<Guid> SeedReferralCode(int redemptionCount)
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
            IsActive = true,
        };
        _db.ReferralCodes.Add(code);
        await _db.SaveChangesAsync();

        for (int i = 0; i < redemptionCount; i++)
        {
            _db.ReferralRedemptions.Add(new ReferralRedemption
            {
                ReferralCodeId  = code.Id,
                NewStudioId     = Guid.NewGuid(),
            });
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return code.Id;
    }
}
