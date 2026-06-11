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
    private readonly FakeDbContext _db          = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant     = Substitute.For<ICurrentTenant>();
    private readonly ICurrentUser   _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid           _studioId   = Guid.NewGuid();
    private readonly Guid           _userId     = Guid.NewGuid();

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
        result.ShareUrl.Should().StartWith("https://penaearte.com/share/");
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

    private async Task<Guid> SeedRevision()
    {
        Design design = new()
        {
            StudioId = _studioId,
            ClientId = Guid.NewGuid(),
            ArtistId = Guid.NewGuid(),
            Title    = "Rose"
        };
        _db.Designs.Add(design);

        DesignRevision revision = new()
        {
            StudioId      = _studioId,
            DesignId      = design.Id,
            VersionNumber = 1,
            FileUrl       = "https://r2.example.com/file.png",
            UploadedAt    = DateTime.UtcNow
        };
        _db.DesignRevisions.Add(revision);
        await _db.SaveChangesAsync();
        return revision.Id;
    }
}
