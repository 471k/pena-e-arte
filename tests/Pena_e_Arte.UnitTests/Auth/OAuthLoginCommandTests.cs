using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Auth;

public class OAuthLoginCommandTests
{
    private readonly IOAuthTokenValidator _validator = Substitute.For<IOAuthTokenValidator>();
    private readonly IIdentityService     _identity  = Substitute.For<IIdentityService>();

    private OAuthLoginHandler CreateSut() => new(_validator, _identity);

    [Fact]
    public async Task Handle_GoogleHappyPath_ReturnsAuthResponse()
    {
        _validator.ValidateGoogleTokenAsync("google-token", Arg.Any<CancellationToken>())
            .Returns(new OAuthUserInfo("user@example.com", "sub-1", "Rui"));
        _identity.LoginWithVerifiedEmailAsync("user@example.com")
            .Returns((true, "access-token", (string?)null));
        _identity.CreateRefreshTokenAsync("user@example.com").Returns("refresh-token");

        AuthResponse response = await CreateSut().Handle(
            new OAuthLoginCommand(new OAuthLoginRequest("google", "google-token")), default);

        response.AccessToken.Should().Be("access-token");
        response.RefreshToken.Should().Be("refresh-token");
    }

    [Fact]
    public async Task Handle_AppleHappyPath_ReturnsAuthResponse()
    {
        _validator.ValidateAppleTokenAsync("apple-token", Arg.Any<CancellationToken>())
            .Returns(new OAuthUserInfo("user@example.com", "sub-1", "Rui"));
        _identity.LoginWithVerifiedEmailAsync("user@example.com")
            .Returns((true, "access-token", (string?)null));
        _identity.CreateRefreshTokenAsync("user@example.com").Returns("refresh-token");

        AuthResponse response = await CreateSut().Handle(
            new OAuthLoginCommand(new OAuthLoginRequest("apple", "apple-token")), default);

        response.AccessToken.Should().Be("access-token");
    }

    [Fact]
    public async Task Handle_UnknownProvider_ThrowsBusinessRuleViolationException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new OAuthLoginCommand(new OAuthLoginRequest("facebook", "token")), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_NoAccountFound_ThrowsBusinessRuleViolationException()
    {
        _validator.ValidateGoogleTokenAsync("google-token", Arg.Any<CancellationToken>())
            .Returns(new OAuthUserInfo("nobody@example.com", "sub-1", null));
        _identity.LoginWithVerifiedEmailAsync("nobody@example.com")
            .Returns((false, (string?)null, "No account found with this email. Please register first."));

        Func<Task> act = () => CreateSut().Handle(
            new OAuthLoginCommand(new OAuthLoginRequest("google", "google-token")), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*No account found*");
    }

    [Fact]
    public async Task Handle_InvalidIdToken_PropagatesException()
    {
        _validator.ValidateGoogleTokenAsync("bad-token", Arg.Any<CancellationToken>())
            .Returns<Task<OAuthUserInfo>>(_ => throw new InvalidOperationException("Invalid Google ID token."));

        Func<Task> act = () => CreateSut().Handle(
            new OAuthLoginCommand(new OAuthLoginRequest("google", "bad-token")), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
