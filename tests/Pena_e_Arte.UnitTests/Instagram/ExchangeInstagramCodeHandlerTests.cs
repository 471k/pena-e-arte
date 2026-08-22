using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Instagram.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Instagram;

public class ExchangeInstagramCodeHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IInstagramService _instagram = Substitute.For<IInstagramService>();
    private readonly ITokenEncryptor _encryptor = Substitute.For<ITokenEncryptor>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Artist _artist;

    public ExchangeInstagramCodeHandlerTests()
    {
        _artist = new Artist
        {
            StudioId = _studioId,
            FirstName = "Ana",
            LastName = "Sousa",
            Email = "ana@test.com",
        };
        _db.Artists.Add(_artist);
        _db.SaveChangesAsync().GetAwaiter().GetResult();

        _instagram.ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new InstagramTokenResponse("token-abc", "bearer", 5184000, "ig-user-123"));
        _instagram.GetUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("inkbyana");
        _encryptor.Encrypt(Arg.Any<string>()).Returns("encrypted-token");
    }

    private ExchangeInstagramCodeHandler CreateSut() =>
        new(_db, _instagram, _encryptor, NullLogger<ExchangeInstagramCodeHandler>.Instance);

    [Fact]
    public async Task Handle_NewConnection_WritesBothInstagramConnectionAndSocialAccountLink()
    {
        await CreateSut().Handle(new ExchangeInstagramCodeCommand(_artist.Id, "auth-code"), default);

        InstagramConnection connection = _db.InstagramConnections.Single();
        connection.Username.Should().Be("inkbyana");
        connection.InstagramUserId.Should().Be("ig-user-123");

        SocialAccountLink link = _db.SocialAccountLinks.Single();
        link.SubjectType.Should().Be(SocialLinkSubjectType.Artist);
        link.SubjectId.Should().Be(_artist.Id);
        link.Platform.Should().Be(SocialPlatform.Instagram);
        link.Handle.Should().Be("inkbyana");
        link.IsVerified.Should().BeTrue();
        link.VerificationMethod.Should().Be(SocialVerificationMethod.OAuthConnect);
        link.ExternalUserId.Should().Be("ig-user-123");
        link.StudioId.Should().Be(_studioId);
    }

    [Fact]
    public async Task Handle_ReconnectingExistingLink_UpdatesRatherThanDuplicates()
    {
        await CreateSut().Handle(new ExchangeInstagramCodeCommand(_artist.Id, "auth-code"), default);

        _instagram.GetUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("inkbyana2");

        await CreateSut().Handle(new ExchangeInstagramCodeCommand(_artist.Id, "auth-code-2"), default);

        _db.SocialAccountLinks.Should().ContainSingle();
        _db.SocialAccountLinks.Single().Handle.Should().Be("inkbyana2");
    }
}
