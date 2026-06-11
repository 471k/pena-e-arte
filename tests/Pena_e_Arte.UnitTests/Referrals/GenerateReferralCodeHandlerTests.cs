using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pena_e_Arte.Application.Referrals.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Referrals;

public class GenerateReferralCodeHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GenerateReferralCodeHandler CreateSut() =>
        new(_db, NullLogger<GenerateReferralCodeHandler>.Instance);

    [Fact]
    public async Task Handle_ValidStudio_ReturnsReferralCodeResponse()
    {
        Guid studioId = await SeedStudio();

        ReferralCodeResponse result =
            await CreateSut().Handle(new GenerateReferralCodeCommand(studioId), default);

        result.Code.Should().HaveLength(8);
        result.Code.Should().MatchRegex("^[A-Z0-9]{8}$");
        result.IsActive.Should().BeTrue();
        result.ShareUrl.Should().Contain(result.Code);
        result.ShareUrl.Should().StartWith("https://penaearte.com/register?ref=");
    }

    [Fact]
    public async Task Handle_ValidStudio_PersistsCodeToDb()
    {
        Guid studioId = await SeedStudio();

        ReferralCodeResponse result =
            await CreateSut().Handle(new GenerateReferralCodeCommand(studioId), default);

        _db.ReferralCodes.Should().ContainSingle(r => r.Code == result.Code && r.StudioId == studioId);
    }

    [Fact]
    public async Task Handle_ExistingActiveCode_DeactivatesOldCodeBeforeCreatingNew()
    {
        Guid studioId = await SeedStudio();
        await CreateSut().Handle(new GenerateReferralCodeCommand(studioId), default);

        ReferralCodeResponse second =
            await CreateSut().Handle(new GenerateReferralCodeCommand(studioId), default);

        int activeCodes = _db.ReferralCodes.Count(r => r.StudioId == studioId && r.IsActive);
        activeCodes.Should().Be(1);
        _db.ReferralCodes.Single(r => r.IsActive && r.StudioId == studioId)
           .Code.Should().Be(second.Code);
    }

    [Fact]
    public async Task Handle_UnknownStudio_ThrowsNotFoundException()
    {
        Func<Task> act = () =>
            CreateSut().Handle(new GenerateReferralCodeCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_CollisionOnFirstAttempt_RetriesAndSucceeds()
    {
        Guid studioId = await SeedStudio();

        // Pre-seed a code that might collide — the handler will find a unique one
        _db.ReferralCodes.Add(new ReferralCode { StudioId = studioId, Code = "AAAAAAAA" });
        await _db.SaveChangesAsync();

        ReferralCodeResponse result =
            await CreateSut().Handle(new GenerateReferralCodeCommand(studioId), default);

        result.Code.Should().HaveLength(8);
        result.IsActive.Should().BeTrue();
    }

    private async Task<Guid> SeedStudio()
    {
        Studio studio = new() { Name = "Test", Slug = "test", City = "Porto", OwnerEmail = "x@x.com", IsActive = true, TrialExpiresAt = DateTime.UtcNow.AddDays(14) };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();
        return studio.Id;
    }
}
