using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Auth;

public class SwitchStudioHandlerTests
{
    private readonly IIdentityService _identity    = Substitute.For<IIdentityService>();
    private readonly FakeDbContext    _db           = FakeDbContext.Create();
    private readonly FakeCurrentUser  _currentUser  = FakeCurrentUser.Client();

    private SwitchStudioHandler CreateSut() => new(
        _db, _identity, _currentUser, NullLogger<SwitchStudioHandler>.Instance);

    private void IdentityIssuesTokens() =>
        _identity.IssueTokensForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((true, "access-token", "refresh-token", (string?)null));

    [Fact]
    public async Task Handle_TargetStudioNotFound_ThrowsNotFoundException()
    {
        IdentityIssuesTokens();

        Func<Task> act = () => CreateSut().Handle(
            new SwitchStudioCommand(new SwitchStudioRequest(Guid.NewGuid())), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ExistingMembership_DoesNotCreateNewClientRow()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId });
        Client existing = new()
        {
            StudioId = studioId, UserId = _currentUser.UserId,
            FirstName = "Ana", LastName = "Rossi", Email = "ana@example.com",
        };
        _db.Clients.Add(existing);
        await _db.SaveChangesAsync();
        IdentityIssuesTokens();

        SwitchStudioResponse response = await CreateSut().Handle(
            new SwitchStudioCommand(new SwitchStudioRequest(studioId)), default);

        response.IsNewMembership.Should().BeFalse();
        _db.Clients.Count(c => c.StudioId == studioId && c.UserId == _currentUser.UserId).Should().Be(1);
    }

    [Fact]
    public async Task Handle_NoExistingMembership_CreatesClientRowFromTemplate()
    {
        Guid homeStudioId   = Guid.NewGuid();
        Guid targetStudioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = targetStudioId });
        Client template = new()
        {
            StudioId = homeStudioId, UserId = _currentUser.UserId,
            FirstName = "Ana", LastName = "Rossi", Email = "ana@example.com", Phone = "555-1234",
        };
        _db.Clients.Add(template);
        await _db.SaveChangesAsync();
        IdentityIssuesTokens();

        SwitchStudioResponse response = await CreateSut().Handle(
            new SwitchStudioCommand(new SwitchStudioRequest(targetStudioId)), default);

        response.IsNewMembership.Should().BeTrue();
        Client created = _db.Clients.Single(c => c.StudioId == targetStudioId);
        created.UserId.Should().Be(_currentUser.UserId);
        created.FirstName.Should().Be("Ana");
        created.LastName.Should().Be("Rossi");
        created.Email.Should().Be("ana@example.com");
        created.Phone.Should().Be("555-1234");
    }

    [Fact]
    public async Task Handle_UserHasNoClientRowAnywhere_CreatesClientRowFromCurrentUserEmail()
    {
        // A studio-less registrant (see RegisterUserHandler) has zero Client rows
        // anywhere — this is their first-ever studio membership, seeded from the
        // account's own email rather than a template Client row.
        Guid targetStudioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = targetStudioId });
        await _db.SaveChangesAsync();
        IdentityIssuesTokens();

        FakeCurrentUser currentUser = new(Guid.NewGuid(), "client", "ana@example.com");
        SwitchStudioHandler sut = new(_db, _identity, currentUser, NullLogger<SwitchStudioHandler>.Instance);

        SwitchStudioResponse response = await sut.Handle(
            new SwitchStudioCommand(new SwitchStudioRequest(targetStudioId)), default);

        response.IsNewMembership.Should().BeTrue();
        Client created = _db.Clients.Single(c => c.StudioId == targetStudioId && c.UserId == currentUser.UserId);
        created.Email.Should().Be("ana@example.com");
        created.FirstName.Should().Be("ana");
        created.LastName.Should().Be(string.Empty);
    }

    [Fact]
    public async Task Handle_UserHasNoClientRowAnywhereAndNoEmail_ThrowsBusinessRuleViolationException()
    {
        Guid targetStudioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = targetStudioId });
        await _db.SaveChangesAsync();
        IdentityIssuesTokens();

        FakeCurrentUser currentUser = new(Guid.NewGuid(), "client", null);
        SwitchStudioHandler sut = new(_db, _identity, currentUser, NullLogger<SwitchStudioHandler>.Instance);

        Func<Task> act = () => sut.Handle(
            new SwitchStudioCommand(new SwitchStudioRequest(targetStudioId)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ValidSwitch_EnsuresClaimAndIssuesTokensForTargetStudio()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId });
        _db.Clients.Add(new Client
        {
            StudioId = studioId, UserId = _currentUser.UserId,
            FirstName = "Ana", LastName = "Rossi", Email = "ana@example.com",
        });
        await _db.SaveChangesAsync();
        IdentityIssuesTokens();

        await CreateSut().Handle(new SwitchStudioCommand(new SwitchStudioRequest(studioId)), default);

        await _identity.Received(1).EnsureTenantClaimAsync(_currentUser.UserId, studioId, Arg.Any<CancellationToken>());
        await _identity.Received(1).IssueTokensForTenantAsync(_currentUser.UserId, studioId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_IdentityIssuanceFails_ThrowsBusinessRuleViolationException()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId });
        _db.Clients.Add(new Client
        {
            StudioId = studioId, UserId = _currentUser.UserId,
            FirstName = "Ana", LastName = "Rossi", Email = "ana@example.com",
        });
        await _db.SaveChangesAsync();
        _identity.IssueTokensForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((false, (string?)null, (string?)null, "User not found."));

        Func<Task> act = () => CreateSut().Handle(
            new SwitchStudioCommand(new SwitchStudioRequest(studioId)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>().WithMessage("User not found.");
    }
}
