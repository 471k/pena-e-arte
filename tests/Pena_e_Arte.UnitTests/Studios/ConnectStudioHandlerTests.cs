using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class ConnectStudioHandlerTests
{
    private readonly FakeDbContext        _db            = FakeDbContext.Create();
    private readonly ICurrentTenant       _tenant        = Substitute.For<ICurrentTenant>();
    private readonly IStripeConnectService _stripeConnect = Substitute.For<IStripeConnectService>();
    private readonly Guid                 _studioId      = Guid.NewGuid();

    public ConnectStudioHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _stripeConnect.CreateConnectedAccountAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("acct_new_123");
        _stripeConnect.CreateAccountLinkAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://connect.stripe.com/onboarding/test");
    }

    private ConnectStudioHandler CreateSut() => new(_db, _tenant, _stripeConnect);

    [Fact]
    public async Task Handle_NoExistingAccount_CreatesStripeConnectAccount()
    {
        await SeedStudio(stripeAccountId: null);

        await CreateSut().Handle(new ConnectStudioCommand(ValidRequest()), default);

        await _stripeConnect.Received(1)
            .CreateConnectedAccountAsync("owner@studio.com", "PT", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoExistingAccount_PersistsStripeAccountIdToStudio()
    {
        await SeedStudio(stripeAccountId: null);

        await CreateSut().Handle(new ConnectStudioCommand(ValidRequest()), default);

        _db.Studios.Single(s => s.Id == _studioId).StripeAccountId.Should().Be("acct_new_123");
    }

    [Fact]
    public async Task Handle_NoExistingAccount_ReturnsOnboardingUrl()
    {
        await SeedStudio(stripeAccountId: null);

        ConnectOnboardingResponse result = await CreateSut()
            .Handle(new ConnectStudioCommand(ValidRequest()), default);

        result.OnboardingUrl.Should().Be("https://connect.stripe.com/onboarding/test");
    }

    [Fact]
    public async Task Handle_ExistingAccount_SkipsAccountCreation()
    {
        await SeedStudio(stripeAccountId: "acct_existing");

        await CreateSut().Handle(new ConnectStudioCommand(ValidRequest()), default);

        await _stripeConnect.DidNotReceive()
            .CreateConnectedAccountAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingAccount_GeneratesNewAccountLinkWithExistingId()
    {
        await SeedStudio(stripeAccountId: "acct_existing");

        await CreateSut().Handle(new ConnectStudioCommand(ValidRequest()), default);

        await _stripeConnect.Received(1)
            .CreateAccountLinkAsync(
                "acct_existing",
                "https://example.com/return",
                "https://example.com/refresh",
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingAccount_ReturnsOnboardingUrl()
    {
        await SeedStudio(stripeAccountId: "acct_existing");

        ConnectOnboardingResponse result = await CreateSut()
            .Handle(new ConnectStudioCommand(ValidRequest()), default);

        result.OnboardingUrl.Should().Be("https://connect.stripe.com/onboarding/test");
    }

    [Fact]
    public async Task Handle_StudioNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut()
            .Handle(new ConnectStudioCommand(ValidRequest()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task SeedStudio(string? stripeAccountId)
    {
        _db.Studios.Add(new Studio
        {
            Id              = _studioId,
            Name            = "Test Studio",
            Slug            = "test",
            OwnerEmail      = "owner@studio.com",
            StripeAccountId = stripeAccountId
        });
        await _db.SaveChangesAsync();
    }

    private static ConnectStudioRequest ValidRequest() =>
        new("https://example.com/return", "https://example.com/refresh", "PT");
}
