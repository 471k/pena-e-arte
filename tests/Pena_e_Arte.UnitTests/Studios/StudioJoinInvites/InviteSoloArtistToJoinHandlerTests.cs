using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Studios.StudioJoinInvites;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios.StudioJoinInvites;

public class InviteSoloArtistToJoinHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IEmailRenderer _emailRenderer = Substitute.For<IEmailRenderer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IAppSettings _appSettings = Substitute.For<IAppSettings>();
    private readonly Guid _studioId = Guid.NewGuid();

    public InviteSoloArtistToJoinHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _appSettings.BaseUrl.Returns("https://tattooos.co");
        _emailRenderer.RenderStudioJoinInvite(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns("<html></html>");
    }

    private InviteSoloArtistToJoinHandler CreateSut() => new(
        _db, _tenant, _identity, _emailRenderer, _notifications, _appSettings,
        NullLogger<InviteSoloArtistToJoinHandler>.Instance);

    private async Task SeedInvitingStudio() =>
        await SeedInvitingStudioWithName("Ink Collective");

    private async Task SeedInvitingStudioWithName(string name)
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = name, Slug = "ink-collective", City = "Lisbon" });
        await _db.SaveChangesAsync();
    }

    private void SeedSoloOwnerAccount(Guid userId, string email)
    {
        _identity.GetUserIdByEmailAsync(email, Arg.Any<CancellationToken>()).Returns(userId);
        _identity.GetUserRolesAsync(userId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>)["owner"]);
    }

    [Fact]
    public async Task Handle_NoAccountForEmail_ThrowsBusinessRuleViolationException()
    {
        await SeedInvitingStudio();
        _identity.GetUserIdByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);

        Func<Task> act = () => CreateSut().Handle(
            new InviteSoloArtistToJoinCommand(new CreateArtistRequest("Jane", "Doe", "jane@example.com", null)),
            default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_AccountIsNotOwnerRole_ThrowsBusinessRuleViolationException()
    {
        await SeedInvitingStudio();
        Guid userId = Guid.NewGuid();
        _identity.GetUserIdByEmailAsync("jane@example.com", Arg.Any<CancellationToken>()).Returns(userId);
        _identity.GetUserRolesAsync(userId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>)["client"]);

        Func<Task> act = () => CreateSut().Handle(
            new InviteSoloArtistToJoinCommand(new CreateArtistRequest("Jane", "Doe", "jane@example.com", null)),
            default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_OwnerAccountHasNoSoloStudio_ThrowsBusinessRuleViolationException()
    {
        await SeedInvitingStudio();
        Guid userId = Guid.NewGuid();
        SeedSoloOwnerAccount(userId, "jane@example.com");
        // Owner role confirmed, but no matching solo Studio row seeded — e.g. a normal
        // (non-solo) studio owner's email, or an already-dissolved solo studio.

        Func<Task> act = () => CreateSut().Handle(
            new InviteSoloArtistToJoinCommand(new CreateArtistRequest("Jane", "Doe", "jane@example.com", null)),
            default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ValidSoloArtistAccount_CreatesPendingInvite()
    {
        await SeedInvitingStudio();
        Guid userId = Guid.NewGuid();
        SeedSoloOwnerAccount(userId, "jane@example.com");
        _db.Studios.Add(new Studio
        {
            Name = "Jane Doe",
            Slug = "jane-doe",
            City = string.Empty,
            OwnerEmail = "jane@example.com",
            IsSolo = true,
            IsActive = true,
        });
        await _db.SaveChangesAsync();

        StudioJoinInviteResponse result = await CreateSut().Handle(
            new InviteSoloArtistToJoinCommand(new CreateArtistRequest("Jane", "Doe", "jane@example.com", null)),
            default);

        result.Status.Should().Be("Pending");
        result.InvitedEmail.Should().Be("jane@example.com");
        _db.StudioJoinInvites.Single().StudioId.Should().Be(_studioId);
    }

    [Fact]
    public async Task Handle_DuplicatePendingInvite_ThrowsBusinessRuleViolationException()
    {
        await SeedInvitingStudio();
        Guid userId = Guid.NewGuid();
        SeedSoloOwnerAccount(userId, "jane@example.com");
        _db.Studios.Add(new Studio
        {
            Name = "Jane Doe",
            Slug = "jane-doe",
            City = string.Empty,
            OwnerEmail = "jane@example.com",
            IsSolo = true,
            IsActive = true,
        });
        await _db.SaveChangesAsync();

        InviteSoloArtistToJoinCommand command = new(new CreateArtistRequest("Jane", "Doe", "jane@example.com", null));
        await CreateSut().Handle(command, default);

        Func<Task> act = () => CreateSut().Handle(command, default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ValidSoloArtistAccount_SendsNotificationEmail()
    {
        await SeedInvitingStudioWithName("Ink Collective");
        Guid userId = Guid.NewGuid();
        SeedSoloOwnerAccount(userId, "jane@example.com");
        _db.Studios.Add(new Studio
        {
            Name = "Jane Doe",
            Slug = "jane-doe",
            City = string.Empty,
            OwnerEmail = "jane@example.com",
            IsSolo = true,
            IsActive = true,
        });
        await _db.SaveChangesAsync();

        await CreateSut().Handle(
            new InviteSoloArtistToJoinCommand(new CreateArtistRequest("Jane", "Doe", "jane@example.com", null)),
            default);

        await _notifications.Received(1).SendEmailAsync(
            "jane@example.com", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
