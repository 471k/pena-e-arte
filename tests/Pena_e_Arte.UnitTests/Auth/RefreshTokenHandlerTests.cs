using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Auth;

public class RefreshTokenHandlerTests
{
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();

    private RefreshTokenHandler CreateSut() => new(_identity);

    [Fact]
    public async Task Handle_ValidRefreshToken_ReturnsNewAuthResponse()
    {
        _identity.RefreshTokenAsync("valid.token")
                 .Returns((true, "new.access.token", "new.refresh.token", (string?)null));

        AuthResponse result = await CreateSut().Handle(
            new RefreshTokenCommand(new RefreshTokenRequest("valid.token")), default);

        result.AccessToken.Should().Be("new.access.token");
        result.RefreshToken.Should().Be("new.refresh.token");
        result.TokenType.Should().Be("Bearer");
    }

    [Fact]
    public async Task Handle_InvalidRefreshToken_ThrowsBusinessRuleViolationException()
    {
        _identity.RefreshTokenAsync(Arg.Any<string>())
                 .Returns((false, null, null, "Invalid refresh token."));

        Func<Task> act = () => CreateSut().Handle(
            new RefreshTokenCommand(new RefreshTokenRequest("bad.token")), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("Invalid refresh token.");
    }

    [Fact]
    public async Task Handle_NullError_ThrowsWithFallbackMessage()
    {
        _identity.RefreshTokenAsync(Arg.Any<string>())
                 .Returns((false, null, null, (string?)null));

        Func<Task> act = () => CreateSut().Handle(
            new RefreshTokenCommand(new RefreshTokenRequest("bad.token")), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("Invalid refresh token.");
    }

    [Fact]
    public async Task Handle_ValidToken_CallsIdentityServiceWithToken()
    {
        _identity.RefreshTokenAsync("my.token")
                 .Returns((true, "access", "refresh", (string?)null));

        await CreateSut().Handle(
            new RefreshTokenCommand(new RefreshTokenRequest("my.token")), default);

        await _identity.Received(1).RefreshTokenAsync("my.token");
    }
}
