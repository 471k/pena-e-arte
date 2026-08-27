using FluentAssertions;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

public class UpdateArtistPortfolioHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Owner();
    private readonly Guid _studioId = Guid.NewGuid();

    private UpdateArtistPortfolioHandler CreateSut() => new(_db, _currentUser);

    private async Task<Artist> SeedArtist(Guid? userId = null)
    {
        Artist artist = new()
        {
            StudioId = _studioId,
            UserId = userId,
            FirstName = "Liku",
            LastName = "Tatuazhisti",
            Email = "liku@protonmail.com",
        };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
        return artist;
    }

    [Fact]
    public async Task Handle_NewImage_PersistsWithGivenStyle()
    {
        Artist artist = await SeedArtist();
        UpdateArtistPortfolioRequest req = new([new PortfolioImageInput("https://img/1.jpg", "realism", null)]);

        ArtistResponse result = await CreateSut().Handle(new UpdateArtistPortfolioCommand(artist.Id, req), default);

        result.PortfolioImages.Should().ContainSingle()
            .Which.Style.Should().Be("realism");
    }

    [Fact]
    public async Task Handle_NewImage_NoStyleGiven_PersistsAsNull()
    {
        Artist artist = await SeedArtist();
        UpdateArtistPortfolioRequest req = new([new PortfolioImageInput("https://img/1.jpg", null, null)]);

        ArtistResponse result = await CreateSut().Handle(new UpdateArtistPortfolioCommand(artist.Id, req), default);

        result.PortfolioImages.Should().ContainSingle()
            .Which.Style.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ExistingUntaggedImage_UpdatingStyle_SetsStyleWithoutRecreatingTheRow()
    {
        Artist artist = await SeedArtist();
        PortfolioImage image = new() { ArtistId = artist.Id, StudioId = _studioId, ImageUrl = "https://img/1.jpg", Style = null };
        _db.PortfolioImages.Add(image);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        UpdateArtistPortfolioRequest req = new([new PortfolioImageInput("https://img/1.jpg", "traditional", null)]);
        await CreateSut().Handle(new UpdateArtistPortfolioCommand(artist.Id, req), default);

        PortfolioImage stored = _db.PortfolioImages.Single(p => p.ArtistId == artist.Id);
        stored.Id.Should().Be(image.Id); // same row — reviews on it are preserved
        stored.Style.Should().Be("traditional");
    }

    [Fact]
    public async Task Handle_ImageNoLongerInRequest_IsRemoved()
    {
        Artist artist = await SeedArtist();
        _db.PortfolioImages.Add(new PortfolioImage { ArtistId = artist.Id, StudioId = _studioId, ImageUrl = "https://img/1.jpg" });
        _db.PortfolioImages.Add(new PortfolioImage { ArtistId = artist.Id, StudioId = _studioId, ImageUrl = "https://img/2.jpg" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        UpdateArtistPortfolioRequest req = new([new PortfolioImageInput("https://img/1.jpg", null, null)]);
        await CreateSut().Handle(new UpdateArtistPortfolioCommand(artist.Id, req), default);

        _db.PortfolioImages.Where(p => p.ArtistId == artist.Id).Should().ContainSingle()
            .Which.ImageUrl.Should().Be("https://img/1.jpg");
    }

    [Fact]
    public async Task Handle_ArtistNotFound_ThrowsNotFoundException()
    {
        UpdateArtistPortfolioRequest req = new([]);

        Func<Task> act = () => CreateSut().Handle(new UpdateArtistPortfolioCommand(Guid.NewGuid(), req), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ArtistEditingAnotherArtistsPortfolio_ThrowsForbidden()
    {
        Artist artist = await SeedArtist(userId: Guid.NewGuid());
        FakeCurrentUser otherArtistUser = FakeCurrentUser.Artist();
        UpdateArtistPortfolioHandler sut = new(_db, otherArtistUser);
        UpdateArtistPortfolioRequest req = new([new PortfolioImageInput("https://img/1.jpg", "realism", null)]);

        Func<Task> act = () => sut.Handle(new UpdateArtistPortfolioCommand(artist.Id, req), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Theory]
    [InlineData(PortfolioImageCategory.FreshTattoo)]
    [InlineData(PortfolioImageCategory.HealedTattoo)]
    [InlineData(PortfolioImageCategory.Design)]
    public async Task Handle_NewImage_PersistsGivenCategory(string category)
    {
        Artist artist = await SeedArtist();
        UpdateArtistPortfolioRequest req = new([new PortfolioImageInput("https://img/1.jpg", null, category)]);

        ArtistResponse result = await CreateSut().Handle(new UpdateArtistPortfolioCommand(artist.Id, req), default);

        result.PortfolioImages.Should().ContainSingle()
            .Which.Category.Should().Be(category);
    }

    [Fact]
    public async Task Handle_ExistingImage_UpdatingCategoryOnly_LeavesStyleUnchanged()
    {
        Artist artist = await SeedArtist();
        PortfolioImage image = new()
        {
            ArtistId = artist.Id,
            StudioId = _studioId,
            ImageUrl = "https://img/1.jpg",
            Style = "realism",
            Category = null,
        };
        _db.PortfolioImages.Add(image);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        UpdateArtistPortfolioRequest req = new([new PortfolioImageInput("https://img/1.jpg", "realism", PortfolioImageCategory.HealedTattoo)]);
        await CreateSut().Handle(new UpdateArtistPortfolioCommand(artist.Id, req), default);

        PortfolioImage stored = _db.PortfolioImages.Single(p => p.ArtistId == artist.Id);
        stored.Style.Should().Be("realism");
        stored.Category.Should().Be(PortfolioImageCategory.HealedTattoo);
    }

    [Fact]
    public async Task Handle_ExistingImage_UpdatingStyleOnly_LeavesCategoryUnchanged()
    {
        Artist artist = await SeedArtist();
        PortfolioImage image = new()
        {
            ArtistId = artist.Id,
            StudioId = _studioId,
            ImageUrl = "https://img/1.jpg",
            Style = null,
            Category = PortfolioImageCategory.FreshTattoo,
        };
        _db.PortfolioImages.Add(image);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        UpdateArtistPortfolioRequest req = new([new PortfolioImageInput("https://img/1.jpg", "blackwork", PortfolioImageCategory.FreshTattoo)]);
        await CreateSut().Handle(new UpdateArtistPortfolioCommand(artist.Id, req), default);

        PortfolioImage stored = _db.PortfolioImages.Single(p => p.ArtistId == artist.Id);
        stored.Style.Should().Be("blackwork");
        stored.Category.Should().Be(PortfolioImageCategory.FreshTattoo);
    }

    [Fact]
    public async Task Handle_ExistingTaggedImage_CategoryOmitted_ClearsBackToUncategorized()
    {
        Artist artist = await SeedArtist();
        PortfolioImage image = new()
        {
            ArtistId = artist.Id,
            StudioId = _studioId,
            ImageUrl = "https://img/1.jpg",
            Category = PortfolioImageCategory.Design,
        };
        _db.PortfolioImages.Add(image);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        UpdateArtistPortfolioRequest req = new([new PortfolioImageInput("https://img/1.jpg", null, null)]);
        await CreateSut().Handle(new UpdateArtistPortfolioCommand(artist.Id, req), default);

        PortfolioImage stored = _db.PortfolioImages.Single(p => p.ArtistId == artist.Id);
        stored.Category.Should().BeNull();
    }
}
