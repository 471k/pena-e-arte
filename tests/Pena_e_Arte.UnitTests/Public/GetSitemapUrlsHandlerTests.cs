using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Public;

public class GetSitemapUrlsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetSitemapUrlsHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_IncludesActiveStudioBySlug()
    {
        _db.Studios.Add(new Studio { Name = "Ink Studio", Slug = "ink-studio", City = "Lisboa", IsActive = true });
        await _db.SaveChangesAsync();

        List<SitemapUrlEntry> result = await CreateSut().Handle(new GetSitemapUrlsQuery(), default);

        result.Should().ContainSingle(u => u.Path == "/s/ink-studio");
    }

    [Fact]
    public async Task Handle_ExcludesInactiveStudio()
    {
        _db.Studios.Add(new Studio { Name = "Closed Studio", Slug = "closed-studio", City = "Porto", IsActive = false });
        await _db.SaveChangesAsync();

        List<SitemapUrlEntry> result = await CreateSut().Handle(new GetSitemapUrlsQuery(), default);

        result.Should().NotContain(u => u.Path == "/s/closed-studio");
    }

    [Fact]
    public async Task Handle_IncludesActiveArtistWithSlug()
    {
        Artist artist = new()
        {
            StudioId  = Guid.NewGuid(),
            FirstName = "Elena",
            LastName  = "Martins",
            Email     = "elena@test.com",
            IsActive  = true,
        };
        artist.SetSlug("elena-martins");
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();

        List<SitemapUrlEntry> result = await CreateSut().Handle(new GetSitemapUrlsQuery(), default);

        result.Should().ContainSingle(u => u.Path == "/artist/elena-martins");
    }

    [Fact]
    public async Task Handle_ExcludesArtistWithoutSlug()
    {
        _db.Artists.Add(new Artist
        {
            StudioId  = Guid.NewGuid(),
            FirstName = "No",
            LastName  = "Slug",
            Email     = "noslug@test.com",
            IsActive  = true,
        });
        await _db.SaveChangesAsync();

        List<SitemapUrlEntry> result = await CreateSut().Handle(new GetSitemapUrlsQuery(), default);

        result.Should().BeEmpty();
    }
}
