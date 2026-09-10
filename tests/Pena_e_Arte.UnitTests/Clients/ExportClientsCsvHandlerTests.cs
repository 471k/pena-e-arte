using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Clients.Queries;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class ExportClientsCsvHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public ExportClientsCsvHandlerTests() => _tenant.StudioId.Returns(_studioId);

    private ExportClientsCsvHandler CreateSut() => new(_db, _tenant);

    [Fact]
    public async Task Handle_EmptyStudio_ReturnsHeaderRowOnly()
    {
        string csv = await CreateSut().Handle(new ExportClientsCsvQuery(), default);

        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().ContainSingle();
        lines[0].Should().StartWith(CsvUtils.Bom + "First Name,Last Name,Email,Phone,Assigned Artist");
    }

    [Fact]
    public async Task Handle_ClientWithArtist_ProducesExpectedRow()
    {
        Guid artistId = await SeedArtist("Luna", "Artista");
        await SeedClient("Jane", "Doe", "jane@example.com", artistId, marketingOptIn: true);

        string csv = await CreateSut().Handle(new ExportClientsCsvQuery(), default);

        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);
        lines[1].Should().Contain("Jane").And.Contain("Doe").And.Contain("jane@example.com")
            .And.Contain("Luna Artista").And.Contain("Yes");
    }

    [Fact]
    public async Task Handle_UnassignedClient_ShowsEmptyArtistColumn()
    {
        await SeedClient("Jane", "Doe", "jane@example.com", null, marketingOptIn: false);

        string csv = await CreateSut().Handle(new ExportClientsCsvQuery(), default);

        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[1].Should().Contain("Jane,Doe,jane@example.com").And.Contain(",No");
    }

    [Fact]
    public async Task Handle_NameContainingCommaAndQuote_IsEscapedCorrectly()
    {
        await SeedClient("Jane, \"JJ\"", "Doe", "jane@example.com", null, marketingOptIn: false);

        string csv = await CreateSut().Handle(new ExportClientsCsvQuery(), default);

        csv.Should().Contain("\"Jane, \"\"JJ\"\"\",Doe");
    }

    [Fact]
    public async Task Handle_OtherStudioClient_ExcludedFromExport()
    {
        _db.Clients.Add(new Client
        {
            StudioId = Guid.NewGuid(),
            FirstName = "Other",
            LastName = "Studio",
            Email = "other@example.com",
        });
        await _db.SaveChangesAsync();

        string csv = await CreateSut().Handle(new ExportClientsCsvQuery(), default);

        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().ContainSingle();
    }

    private async Task<Guid> SeedArtist(string firstName, string lastName)
    {
        Artist artist = new()
        {
            StudioId = _studioId,
            FirstName = firstName,
            LastName = lastName,
            Email = $"{Guid.NewGuid():N}@test.com",
        };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
        return artist.Id;
    }

    private async Task SeedClient(
        string firstName, string lastName, string email, Guid? artistId, bool marketingOptIn)
    {
        _db.Clients.Add(new Client
        {
            StudioId = _studioId,
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            ArtistId = artistId,
            MarketingOptIn = marketingOptIn,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
