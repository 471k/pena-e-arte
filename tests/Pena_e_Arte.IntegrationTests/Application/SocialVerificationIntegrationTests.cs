using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pena_e_Arte.Application.Social.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// Covers the anonymous OAuth-callback path (ExchangeSocialOAuthCodeCommand) against a
/// real database — in particular the suspended-studio check, which is the exact class
/// of bug already fixed twice for the artist Instagram path (see architecture.md
/// Decisions Log). Uses a stub ISocialOAuthProvider so no real network calls happen.
/// </summary>
[Collection("Database")]
public class SocialVerificationIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task ExchangeSocialOAuthCode_ActiveStudio_WritesVerifiedSocialAccountLink()
    {
        Studio studio = await SeedStudio(isActive: true);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        ExchangeSocialOAuthCodeHandler handler = CreateSut(db);

        await handler.Handle(
            new ExchangeSocialOAuthCodeCommand(SocialLinkSubjectType.Studio, studio.Id, SocialPlatform.TikTok, "auth-code"),
            default);

        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        SocialAccountLink link = verify.SocialAccountLinks.Single(s => s.SubjectId == studio.Id);
        link.SubjectType.Should().Be(SocialLinkSubjectType.Studio);
        link.SubjectId.Should().Be(studio.Id);
        link.StudioId.Should().Be(studio.Id);
        link.Platform.Should().Be(SocialPlatform.TikTok);
        link.Handle.Should().Be("stub-username");
        link.IsVerified.Should().BeTrue();
        link.VerificationMethod.Should().Be(SocialVerificationMethod.OAuthConnect);
        // Decision 3: no ongoing sync need for a studio — token discarded, not persisted.
        link.EncryptedToken.Should().BeNull();
    }

    [Fact]
    public async Task ExchangeSocialOAuthCode_SuspendedStudio_ThrowsAndWritesNothing()
    {
        Studio studio = await SeedStudio(isActive: false);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        ExchangeSocialOAuthCodeHandler handler = CreateSut(db);

        Func<Task> act = () => handler.Handle(
            new ExchangeSocialOAuthCodeCommand(SocialLinkSubjectType.Studio, studio.Id, SocialPlatform.TikTok, "auth-code"),
            default);

        await act.Should().ThrowAsync<NotFoundException>();

        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        verify.SocialAccountLinks.Where(s => s.SubjectId == studio.Id).Should().BeEmpty();
    }

    [Fact]
    public async Task ExchangeSocialOAuthCode_ArtistSubjectInSuspendedStudio_ThrowsAndWritesNothing()
    {
        Studio studio = await SeedStudio(isActive: false);
        Artist artist = await SeedArtist(studio.Id, "suspended-studio-artist");

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        ExchangeSocialOAuthCodeHandler handler = CreateSut(db);

        Func<Task> act = () => handler.Handle(
            new ExchangeSocialOAuthCodeCommand(SocialLinkSubjectType.Artist, artist.Id, SocialPlatform.TikTok, "auth-code"),
            default);

        await act.Should().ThrowAsync<NotFoundException>();

        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        verify.SocialAccountLinks.Where(s => s.SubjectId == artist.Id).Should().BeEmpty();
    }

    [Fact]
    public async Task ExchangeSocialOAuthCode_ArtistSubjectActiveStudio_ResolvesRealStudioIdFromArtist()
    {
        Studio studio = await SeedStudio(isActive: true);
        Artist artist = await SeedArtist(studio.Id, "tiktok-artist");

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        ExchangeSocialOAuthCodeHandler handler = CreateSut(db);

        await handler.Handle(
            new ExchangeSocialOAuthCodeCommand(SocialLinkSubjectType.Artist, artist.Id, SocialPlatform.TikTok, "auth-code"),
            default);

        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        SocialAccountLink link = verify.SocialAccountLinks.Single(s => s.SubjectId == artist.Id);
        link.StudioId.Should().Be(studio.Id);
        // Artist-subject OAuth links keep the token (re-verification job cadence), unlike Studio-subject.
        link.EncryptedToken.Should().NotBeNull();
    }

    private static ExchangeSocialOAuthCodeHandler CreateSut(AppDbContext db) =>
        new(db, new StubSocialOAuthProviderFactory(), new StubTokenEncryptor(),
            NullLogger<ExchangeSocialOAuthCodeHandler>.Instance);

    private async Task<Studio> SeedStudio(bool isActive)
    {
        await using AppDbContext seed = fixture.CreateDbContext(Guid.Empty);
        Studio studio = new()
        {
            Name = "Social Verification Studio",
            Slug = "social-" + Guid.NewGuid().ToString("N")[..8],
            City = "Lisboa",
            IsActive = isActive,
        };
        seed.Studios.Add(studio);
        await seed.SaveChangesAsync();
        return studio;
    }

    private async Task<Artist> SeedArtist(Guid studioId, string slug)
    {
        await using AppDbContext seed = fixture.CreateDbContext(Guid.Empty);
        Artist artist = new()
        {
            StudioId = studioId,
            FirstName = "Test",
            LastName = "Artist",
            Email = $"{slug}@test.com",
        };
        artist.SetSlug(slug);
        seed.Artists.Add(artist);
        await seed.SaveChangesAsync();
        return artist;
    }

    private sealed class StubSocialOAuthProviderFactory : ISocialOAuthProviderFactory
    {
        public ISocialOAuthProvider GetProvider(SocialPlatform platform) => new StubSocialOAuthProvider(platform);
    }

    private sealed class StubSocialOAuthProvider(SocialPlatform platform) : ISocialOAuthProvider
    {
        public SocialPlatform Platform => platform;
        public bool IsConfigured => true;
        public string BuildAuthorizationUrl(string state) => $"https://example.com/authorize?state={state}";

        public Task<SocialOAuthTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct) =>
            Task.FromResult(new SocialOAuthTokenResponse("stub-access-token", "stub-external-id", DateTime.UtcNow.AddDays(60)));

        public Task<string> GetUsernameAsync(string accessToken, CancellationToken ct) =>
            Task.FromResult("stub-username");
    }

    private sealed class StubTokenEncryptor : ITokenEncryptor
    {
        public string Encrypt(string plainText) => $"encrypted:{plainText}";
        public string Decrypt(string cipherText) => cipherText.Replace("encrypted:", "");
    }
}
