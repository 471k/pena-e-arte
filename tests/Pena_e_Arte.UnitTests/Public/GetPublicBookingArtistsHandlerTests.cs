using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Public;

public class GetPublicBookingArtistsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetPublicBookingArtistsHandler CreateSut() => new(_db);

    private static Studio MakeStudio(string slug = "guest-studio", bool active = true, bool published = true) => new()
    {
        Name = "Guest Studio",
        Slug = slug,
        City = "Porto",
        IsActive = active,
        IsPublished = published,
    };

    [Fact]
    public async Task Handle_PublishedStudioWithActiveArtist_ReturnsArtist()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        _db.Artists.Add(new Artist
        {
            StudioId = studio.Id,
            FirstName = "Luna",
            LastName = "Artista",
            Email = "luna@test.com",
            Specializations = "Neo-trad",
            HourlyRate = 80,
            IsActive = true,
        });
        await _db.SaveChangesAsync();

        IReadOnlyList<PublicBookingArtistResponse> result =
            await CreateSut().Handle(new GetPublicBookingArtistsQuery(studio.Slug), default);

        result.Should().ContainSingle();
        result[0].Name.Should().Be("Luna Artista");
        result[0].HourlyRate.Should().Be(80);
        result[0].Specializations.Should().Be("Neo-trad");
    }

    [Fact]
    public async Task Handle_InactiveArtist_IsExcluded()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        _db.Artists.Add(new Artist
        {
            StudioId = studio.Id,
            FirstName = "Retired",
            LastName = "Artist",
            Email = "retired@test.com",
            IsActive = false,
        });
        await _db.SaveChangesAsync();

        IReadOnlyList<PublicBookingArtistResponse> result =
            await CreateSut().Handle(new GetPublicBookingArtistsQuery(studio.Slug), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UnknownSlug_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetPublicBookingArtistsQuery("no-such-slug"), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_UnpublishedStudio_ThrowsNotFoundException()
    {
        Studio studio = MakeStudio(published: false);
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(new GetPublicBookingArtistsQuery(studio.Slug), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_InactiveStudio_ThrowsNotFoundException()
    {
        Studio studio = MakeStudio(active: false);
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(new GetPublicBookingArtistsQuery(studio.Slug), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
