using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Auth;

public class LoginHandlerTests
{
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();

    private LoginHandler CreateSut() => new(_identity);

    [Fact]
    public async Task Handle_ValidCredentials_ReturnsAuthResponseWithToken()
    {
        _identity.LoginAsync("user@example.com", "password123")
                 .Returns((true, "jwt.token.here", null));
        _identity.CreateRefreshTokenAsync("user@example.com")
                 .Returns("refresh.token.here");

        AuthResponse result = await CreateSut().Handle(
            new LoginCommand(new LoginRequest("user@example.com", "password123")), default);

        result.AccessToken.Should().Be("jwt.token.here");
        result.RefreshToken.Should().Be("refresh.token.here");
        result.TokenType.Should().Be("Bearer");
    }

    [Fact]
    public async Task Handle_InvalidCredentials_ThrowsBusinessRuleViolationException()
    {
        _identity.LoginAsync(Arg.Any<string>(), Arg.Any<string>())
                 .Returns((false, null, "Invalid credentials."));

        Func<Task> act = () => CreateSut().Handle(
            new LoginCommand(new LoginRequest("bad@example.com", "wrongpass")), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("Invalid credentials.");
    }

    [Fact]
    public async Task Handle_IdentityServiceReturnsNullError_ThrowsWithFallbackMessage()
    {
        _identity.LoginAsync(Arg.Any<string>(), Arg.Any<string>())
                 .Returns((false, null, (string?)null));

        Func<Task> act = () => CreateSut().Handle(
            new LoginCommand(new LoginRequest("x@x.com", "pass")), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("Invalid credentials.");
    }

    [Fact]
    public async Task Handle_ValidCredentials_CallsIdentityServiceWithCorrectArguments()
    {
        _identity.LoginAsync("user@example.com", "secret123")
                 .Returns((true, "token", null));
        _identity.CreateRefreshTokenAsync("user@example.com")
                 .Returns("refresh.token");

        await CreateSut().Handle(
            new LoginCommand(new LoginRequest("user@example.com", "secret123")), default);

        await _identity.Received(1).LoginAsync("user@example.com", "secret123");
    }
}
