using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;
using StackExchange.Redis;

namespace Pena_e_Arte.IntegrationTests.Application;

// Artist.Specializations is a List<string> mapped through a JSON value converter (see
// ArtistConfiguration). Value converters are invisible to LINQ query translation, so anything
// beyond a simple round trip needs to be proven against a real MySQL database, not just the
// EF Core InMemory provider used by the unit tests — InMemory happens to reject the same
// untranslatable expressions real MySQL/Pomelo would, but that's incidental, not guaranteed.
[Collection("Database")]
public class ArtistSpecializationsIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Specializations_RoundTripsThroughRealMySql()
    {
        Guid studioId = Guid.NewGuid();
        Guid artistId;

        await using (AppDbContext db = fixture.CreateDbContext(studioId))
        {
            Artist artist = new()
            {
                StudioId = studioId,
                FirstName = "Ana",
                LastName = "Lima",
                Email = $"{Guid.NewGuid():N}@studio.com",
                Specializations = [TattooStyle.Blackwork, TattooStyle.Geometric],
            };
            db.Artists.Add(artist);
            await db.SaveChangesAsync();
            artistId = artist.Id;
        }

        await using AppDbContext verifyDb = fixture.CreateDbContext(studioId);
        Artist reloaded = await verifyDb.Artists.SingleAsync(a => a.Id == artistId);

        reloaded.Specializations.Should().BeEquivalentTo([TattooStyle.Blackwork, TattooStyle.Geometric]);
    }

    [Fact]
    public async Task Specializations_EmptyList_RoundTripsAsEmpty_NotNull()
    {
        Guid studioId = Guid.NewGuid();
        Guid artistId;

        await using (AppDbContext db = fixture.CreateDbContext(studioId))
        {
            Artist artist = new()
            {
                StudioId = studioId,
                FirstName = "Rui",
                LastName = "Costa",
                Email = $"{Guid.NewGuid():N}@studio.com",
            };
            db.Artists.Add(artist);
            await db.SaveChangesAsync();
            artistId = artist.Id;
        }

        await using AppDbContext verifyDb = fixture.CreateDbContext(studioId);
        Artist reloaded = await verifyDb.Artists.SingleAsync(a => a.Id == artistId);

        reloaded.Specializations.Should().NotBeNull().And.BeEmpty();
    }

    // Proves GetPortfolioFeedHandler's search-by-specializations branch (which deliberately
    // avoids any LINQ predicate over the converted List<string> column — see the comment in
    // GetPortfolioFeedQuery.cs) actually executes against real MySQL, not just InMemory.
    [Fact]
    public async Task PortfolioFeedSearch_MatchesArtistBySpecialization_AgainstRealMySql()
    {
        Guid studioId = Guid.NewGuid();
        await using AppDbContext db = fixture.CreateDbContext(studioId);

        db.Studios.Add(new Studio
        {
            Id = studioId, Name = "Ink Palace", Slug = $"ink-{Guid.NewGuid():N}",
            City = "Lisbon", Latitude = 38.7169, Longitude = -9.1395, IsActive = true,
        });

        Artist artist = new()
        {
            StudioId = studioId,
            FirstName = "Ana",
            LastName = "Lima",
            Email = $"{Guid.NewGuid():N}@studio.com",
            Specializations = [TattooStyle.Japanese],
        };
        artist.SetSlug($"ana-{Guid.NewGuid():N}");
        db.Artists.Add(artist);
        await db.SaveChangesAsync();

        db.PortfolioImages.Add(new PortfolioImage
        {
            ArtistId = artist.Id, StudioId = studioId, ImageUrl = "img.jpg", Style = "blackwork",
        });
        await db.SaveChangesAsync();

        IConnectionMultiplexer redis = Substitute.For<IConnectionMultiplexer>();
        IDatabase redisDb = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(redisDb);
        redisDb.StringGetAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>())
               .Returns(Task.FromResult(new RedisValue[100]));

        GetPortfolioFeedHandler handler = new(db, redis);

        List<PortfolioImageResponse> result = await handler.Handle(
            new GetPortfolioFeedQuery(null, null, 50, 1, Search: "japan"), CancellationToken.None);

        result.Should().ContainSingle(r => r.ArtistSlug == artist.Slug);
    }
}
