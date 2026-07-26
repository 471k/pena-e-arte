using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Designs;

public class GetSharedDesignHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IR2Service _r2 = Substitute.For<IR2Service>();

    public GetSharedDesignHandlerTests() =>
        _r2.GeneratePresignedReadUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns("https://r2.example.com/signed-url");

    private GetSharedDesignHandler CreateSut() => new(_db, _r2);

    [Fact]
    public async Task Handle_ValidToken_ReturnsSharedDesignResponse()
    {
        string token = await SeedToken(isRevoked: false, daysOffset: 30);

        SharedDesignResponse? result = await CreateSut()
            .Handle(new GetSharedDesignQuery(token), default);

        result.Should().NotBeNull();
        result!.ImageUrl.Should().Be("https://r2.example.com/signed-url");
    }

    [Fact]
    public async Task Handle_ExpiredToken_ReturnsNull()
    {
        string token = await SeedToken(isRevoked: false, daysOffset: -1);

        SharedDesignResponse? result = await CreateSut()
            .Handle(new GetSharedDesignQuery(token), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_RevokedToken_ReturnsNull()
    {
        string token = await SeedToken(isRevoked: true, daysOffset: 30);

        SharedDesignResponse? result = await CreateSut()
            .Handle(new GetSharedDesignQuery(token), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ValidToken_IncrementsViewCount()
    {
        string token = await SeedToken(isRevoked: false, daysOffset: 30);

        await CreateSut().Handle(new GetSharedDesignQuery(token), default);
        await CreateSut().Handle(new GetSharedDesignQuery(token), default);

        DesignShareToken shareToken = _db.DesignShareTokens.First(t => t.Token == token);
        shareToken.ViewCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_UnknownToken_ReturnsNull()
    {
        SharedDesignResponse? result = await CreateSut()
            .Handle(new GetSharedDesignQuery("nonexistenttokenthatdoesnotexist"), default);

        result.Should().BeNull();
    }

    private async Task<string> SeedToken(bool isRevoked, int daysOffset)
    {
        Guid studioId = Guid.NewGuid();

        _db.Studios.Add(new Pena_e_Arte.Domain.Entities.Studio
        {
            Id = studioId,
            Name = "Test Studio",
            Slug = $"test-{studioId:N}",
            City = "Lisboa",
            OwnerEmail = "test@test.com",
            IsActive = true,
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
        });

        Design design = new()
        {
            StudioId = studioId,
            ClientId = Guid.NewGuid(),
            ArtistId = Guid.NewGuid(),
            Title = "Rose"
        };
        _db.Designs.Add(design);

        DesignRevision revision = new()
        {
            StudioId = studioId,
            DesignId = design.Id,
            VersionNumber = 1,
            FileUrl = "https://r2.example.com/file.png",
            UploadedAt = DateTime.UtcNow
        };
        _db.DesignRevisions.Add(revision);

        string token = Guid.NewGuid().ToString("N");
        DesignShareToken shareToken = new()
        {
            StudioId = studioId,
            Token = token,
            DesignRevisionId = revision.Id,
            CreatedByUserId = Guid.NewGuid(),
            ExpiresAt = DateTime.UtcNow.AddDays(daysOffset),
            IsRevoked = isRevoked
        };
        _db.DesignShareTokens.Add(shareToken);
        await _db.SaveChangesAsync();

        return token;
    }
}
