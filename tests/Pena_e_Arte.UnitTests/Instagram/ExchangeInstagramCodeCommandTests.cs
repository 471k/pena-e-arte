using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Pena_e_Arte.Application.Instagram.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Instagram;

public class ExchangeInstagramCodeCommandTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IInstagramService _instagram = Substitute.For<IInstagramService>();
    private readonly ITokenEncryptor _encryptor = Substitute.For<ITokenEncryptor>();
    private readonly ILogger<ExchangeInstagramCodeHandler> _logger =
        Substitute.For<ILogger<ExchangeInstagramCodeHandler>>();
    private readonly Guid _studioId = Guid.NewGuid();

    private ExchangeInstagramCodeHandler CreateSut() => new(_db, _instagram, _encryptor, _logger);

    private async Task<Guid> SeedArtist()
    {
        Artist artist = new()
        {
            StudioId = _studioId,
            FirstName = "Rui",
            LastName = "Tavares",
            Email = "rui@studio.com",
        };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
        return artist.Id;
    }

    private void MockExchange(string accessToken = "access-token", string username = "artist_ig")
    {
        _instagram.ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new InstagramTokenResponse(accessToken, "bearer", 5_184_000, "ig-user-1"));
        _instagram.GetUsernameAsync(accessToken, Arg.Any<CancellationToken>()).Returns(username);
        _encryptor.Encrypt(accessToken).Returns("encrypted-" + accessToken);
    }

    [Fact]
    public async Task Handle_NewArtist_InsertsInstagramConnection()
    {
        Guid artistId = await SeedArtist();
        MockExchange();

        await CreateSut().Handle(new ExchangeInstagramCodeCommand(artistId, "auth-code"), default);

        _db.InstagramConnections.Should().ContainSingle(c =>
            c.ArtistId == artistId &&
            c.Username == "artist_ig" &&
            c.EncryptedToken == "encrypted-access-token" &&
            c.IsActive);
    }

    [Fact]
    public async Task Handle_NewArtist_ResolvesStudioIdFromArtistRecord()
    {
        Guid artistId = await SeedArtist();
        MockExchange();

        await CreateSut().Handle(new ExchangeInstagramCodeCommand(artistId, "auth-code"), default);

        _db.InstagramConnections.Single(c => c.ArtistId == artistId).StudioId.Should().Be(_studioId);
    }

    [Fact]
    public async Task Handle_ExistingInactiveConnection_UpsertsAndReactivates()
    {
        Guid artistId = await SeedArtist();
        _db.InstagramConnections.Add(new InstagramConnection
        {
            StudioId = _studioId,
            ArtistId = artistId,
            InstagramUserId = "old-ig-user",
            Username = "old_username",
            EncryptedToken = "old-encrypted-token",
            TokenExpiresAt = DateTime.UtcNow.AddDays(-1),
            IsActive = false,
        });
        await _db.SaveChangesAsync();

        MockExchange(accessToken: "new-access-token", username: "new_username");

        await CreateSut().Handle(new ExchangeInstagramCodeCommand(artistId, "auth-code"), default);

        InstagramConnection updated = _db.InstagramConnections.Single(c => c.ArtistId == artistId);
        updated.Username.Should().Be("new_username");
        updated.EncryptedToken.Should().Be("encrypted-new-access-token");
        updated.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ArtistDoesNotExist_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new ExchangeInstagramCodeCommand(Guid.NewGuid(), "auth-code"), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
