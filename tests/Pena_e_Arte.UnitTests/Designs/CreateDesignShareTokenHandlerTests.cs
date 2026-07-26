using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Designs;

public class CreateDesignShareTokenHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public CreateDesignShareTokenHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _currentUser.UserId.Returns(_userId);
    }

    private CreateDesignShareTokenHandler CreateSut() => new(_db, _tenant, _currentUser);

    [Fact]
    public async Task Handle_ValidRevision_CreatesTokenWithCorrectProperties()
    {
        Guid revisionId = await SeedRevision();

        DesignShareTokenResponse result = await CreateSut()
            .Handle(new CreateDesignShareTokenCommand(revisionId), default);

        result.Token.Should().HaveLength(32);
        result.ShareUrl.Should().StartWith("https://tattooos.co/share/");
        result.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Handle_ValidRevision_PersistsTokenToDatabase()
    {
        Guid revisionId = await SeedRevision();

        DesignShareTokenResponse result = await CreateSut()
            .Handle(new CreateDesignShareTokenCommand(revisionId), default);

        DesignShareToken? saved = _db.DesignShareTokens.FirstOrDefault(t => t.Id == result.Id);
        saved.Should().NotBeNull();
        saved!.DesignRevisionId.Should().Be(revisionId);
        saved.StudioId.Should().Be(_studioId);
        saved.CreatedByUserId.Should().Be(_userId);
        saved.IsRevoked.Should().BeFalse();
        saved.ViewCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_RevisionNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut()
            .Handle(new CreateDesignShareTokenCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ActiveTokenAlreadyExists_ReturnsExistingTokenInstead()
    {
        Guid revisionId = await SeedRevision();

        DesignShareTokenResponse first = await CreateSut()
            .Handle(new CreateDesignShareTokenCommand(revisionId), default);
        DesignShareTokenResponse second = await CreateSut()
            .Handle(new CreateDesignShareTokenCommand(revisionId), default);

        second.Id.Should().Be(first.Id);
        second.Token.Should().Be(first.Token);
        _db.DesignShareTokens.Count(t => t.DesignRevisionId == revisionId).Should().Be(1);
    }

    [Fact]
    public async Task Handle_PreviousTokenRevoked_CreatesNewToken()
    {
        Guid revisionId = await SeedRevision();

        DesignShareTokenResponse first = await CreateSut()
            .Handle(new CreateDesignShareTokenCommand(revisionId), default);
        _db.DesignShareTokens.First(t => t.Id == first.Id).IsRevoked = true;
        await _db.SaveChangesAsync();

        DesignShareTokenResponse second = await CreateSut()
            .Handle(new CreateDesignShareTokenCommand(revisionId), default);

        second.Id.Should().NotBe(first.Id);
        _db.DesignShareTokens.Count(t => t.DesignRevisionId == revisionId).Should().Be(2);
    }

    [Fact]
    public async Task Handle_ArtistNotOwningDesign_ThrowsForbidden()
    {
        Guid revisionId = await SeedRevision();
        _currentUser.Role.Returns("artist");

        Func<Task> act = () => CreateSut().Handle(new CreateDesignShareTokenCommand(revisionId), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    private async Task<Guid> SeedRevision(Guid? artistUserId = null)
    {
        Design design = new()
        {
            StudioId = _studioId,
            ClientId = Guid.NewGuid(),
            ArtistId = Guid.NewGuid(),
            Title = "Rose"
        };
        _db.Designs.Add(design);

        if (artistUserId.HasValue)
        {
            _db.Artists.Add(new Artist
            {
                Id = design.ArtistId,
                StudioId = _studioId,
                UserId = artistUserId.Value,
                FirstName = "Art",
                LastName = "Ist",
                Email = $"{Guid.NewGuid()}@test.com",
            });
        }

        DesignRevision revision = new()
        {
            StudioId = _studioId,
            DesignId = design.Id,
            VersionNumber = 1,
            FileUrl = "https://r2.example.com/file.png",
            UploadedAt = DateTime.UtcNow
        };
        _db.DesignRevisions.Add(revision);
        await _db.SaveChangesAsync();
        return revision.Id;
    }
}
