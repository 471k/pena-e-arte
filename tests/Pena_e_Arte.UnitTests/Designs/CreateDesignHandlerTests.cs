using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Designs;

public class CreateDesignHandlerTests
{
    private readonly FakeDbContext   _db          = FakeDbContext.Create();
    private readonly ICurrentTenant  _tenant      = Substitute.For<ICurrentTenant>();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Owner();
    private readonly Guid            _studioId    = Guid.NewGuid();

    public CreateDesignHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private CreateDesignHandler CreateSut() => new(_db, _tenant, _currentUser);

    [Fact]
    public async Task Handle_ValidRequest_ReturnsDesignResponse()
    {
        CreateDesignRequest req = new(Guid.NewGuid(), Guid.NewGuid(), "Rose tattoo", "Small red rose on wrist");

        DesignResponse result = await CreateSut().Handle(new CreateDesignCommand(req), default);

        result.Title.Should().Be(req.Title);
        result.Description.Should().Be(req.Description);
        result.ClientId.Should().Be(req.ClientId);
        result.ArtistId.Should().Be(req.ArtistId);
        result.StudioId.Should().Be(_studioId);
        result.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_ValidRequest_PersistsDesignToDb()
    {
        CreateDesignRequest req = new(Guid.NewGuid(), Guid.NewGuid(), "Rose tattoo", null);

        await CreateSut().Handle(new CreateDesignCommand(req), default);

        _db.Designs.Should().ContainSingle(d => d.Title == "Rose tattoo" && d.StudioId == _studioId);
    }

    [Fact]
    public async Task Handle_NullDescription_PersistsNullDescription()
    {
        CreateDesignRequest req = new(Guid.NewGuid(), Guid.NewGuid(), "Minimal", null);

        await CreateSut().Handle(new CreateDesignCommand(req), default);

        _db.Designs.Single().Description.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ArtistCaller_OverridesRequestedArtistIdWithOwnArtistId()
    {
        FakeCurrentUser artistUser = FakeCurrentUser.Artist();
        var artist = new Domain.Entities.Artist
        {
            StudioId  = _studioId,
            UserId    = artistUser.UserId,
            FirstName = "Art",
            LastName  = "Ist",
            Email     = $"{Guid.NewGuid()}@test.com",
        };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();

        CreateDesignRequest req = new(Guid.NewGuid(), Guid.NewGuid(), "Rose tattoo", null);
        CreateDesignHandler sut = new(_db, _tenant, artistUser);

        DesignResponse result = await sut.Handle(new CreateDesignCommand(req), default);

        result.ArtistId.Should().Be(artist.Id);
        result.ArtistId.Should().NotBe(req.ArtistId);
    }

    [Fact]
    public async Task Handle_ArtistCallerWithNoArtistRecord_ThrowsForbidden()
    {
        FakeCurrentUser artistUser = FakeCurrentUser.Artist();
        CreateDesignRequest req = new(Guid.NewGuid(), Guid.NewGuid(), "Rose tattoo", null);
        CreateDesignHandler sut = new(_db, _tenant, artistUser);

        Func<Task> act = () => sut.Handle(new CreateDesignCommand(req), default);

        await act.Should().ThrowAsync<Pena_e_Arte.Domain.Exceptions.ForbiddenException>();
    }
}
