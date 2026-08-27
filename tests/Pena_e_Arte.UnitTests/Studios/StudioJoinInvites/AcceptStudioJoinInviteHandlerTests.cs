using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Studios.StudioJoinInvites;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios.StudioJoinInvites;

public class AcceptStudioJoinInviteHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly FakeCurrentUser _currentUser = new(Guid.NewGuid(), "owner", "jane@example.com");
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IPlanLimitService _planLimits = Substitute.For<IPlanLimitService>();
    private readonly Guid _newStudioId = Guid.NewGuid();
    private readonly Guid _oldStudioId = Guid.NewGuid();

    public AcceptStudioJoinInviteHandlerTests()
    {
        _identity.IssueTokensForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((true, "access-token", "refresh-token", (string?)null));
    }

    private AcceptStudioJoinInviteHandler CreateSut() => new(
        _db, _currentUser, _identity, _planLimits, NullLogger<AcceptStudioJoinInviteHandler>.Instance);

    private async Task<Guid> SeedPendingInviteAndSoloStudio(
        DateTime? expiresAt = null, bool soloStudioStillOwned = true, bool newStudioActive = true)
    {
        _db.Studios.Add(new Studio
        {
            Id = _newStudioId,
            Name = "Ink Collective",
            Slug = "ink-collective",
            City = "Lisbon",
            IsActive = newStudioActive,
        });
        if (soloStudioStillOwned)
        {
            _db.Studios.Add(new Studio
            {
                Id = _oldStudioId,
                Name = "Jane Doe",
                Slug = "jane-doe",
                City = string.Empty,
                OwnerEmail = "jane@example.com",
                IsSolo = true,
                IsActive = true,
            });
        }

        StudioJoinInvite invite = new()
        {
            StudioId = _newStudioId,
            InvitedEmail = "jane@example.com",
            FirstName = "Jane",
            LastName = "Doe",
            Specializations = "Blackwork",
            HourlyRate = 80m,
            Status = StudioJoinInviteStatus.Pending,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(1),
        };
        _db.StudioJoinInvites.Add(invite);
        await _db.SaveChangesAsync();
        return invite.Id;
    }

    [Fact]
    public async Task Handle_HappyPath_CreatesArtistAtNewStudio()
    {
        Guid inviteId = await SeedPendingInviteAndSoloStudio();

        AuthResponse result = await CreateSut().Handle(new AcceptStudioJoinInviteCommand(inviteId), default);

        result.AccessToken.Should().Be("access-token");
        Artist artist = _db.Artists.Single();
        artist.StudioId.Should().Be(_newStudioId);
        artist.UserId.Should().Be(_currentUser.UserId);
        artist.Specializations.Should().Be("Blackwork");
        artist.HourlyRate.Should().Be(80m);
    }

    [Fact]
    public async Task Handle_HappyPath_ClosesOldSoloStudio()
    {
        Guid inviteId = await SeedPendingInviteAndSoloStudio();

        await CreateSut().Handle(new AcceptStudioJoinInviteCommand(inviteId), default);

        Studio oldStudio = _db.Studios.Single(s => s.Id == _oldStudioId);
        oldStudio.IsActive.Should().BeFalse();
        oldStudio.ClosedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_HappyPath_MarksInviteAccepted()
    {
        Guid inviteId = await SeedPendingInviteAndSoloStudio();

        await CreateSut().Handle(new AcceptStudioJoinInviteCommand(inviteId), default);

        _db.StudioJoinInvites.Single(i => i.Id == inviteId).Status.Should().Be(StudioJoinInviteStatus.Accepted);
    }

    [Fact]
    public async Task Handle_HappyPath_SwapsRoleAndTenantClaims()
    {
        Guid inviteId = await SeedPendingInviteAndSoloStudio();

        await CreateSut().Handle(new AcceptStudioJoinInviteCommand(inviteId), default);

        await _identity.Received(1).SwapRoleAsync(_currentUser.UserId, "owner", "artist", Arg.Any<CancellationToken>());
        await _identity.Received(1).RemoveTenantClaimAsync(_currentUser.UserId, _oldStudioId, Arg.Any<CancellationToken>());
        await _identity.Received(1).EnsureTenantClaimAsync(_currentUser.UserId, _newStudioId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_HappyPath_ChecksQuotaScopedToInviteStudioNotCurrentTenant()
    {
        Guid inviteId = await SeedPendingInviteAndSoloStudio();

        await CreateSut().Handle(new AcceptStudioJoinInviteCommand(inviteId), default);

        await _planLimits.Received(1)
            .EnsureWithinLimitAsync(_newStudioId, QuotaType.Artists, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_QuotaExceeded_ThrowsAndDoesNotCreateArtist()
    {
        Guid inviteId = await SeedPendingInviteAndSoloStudio();
        _planLimits.EnsureWithinLimitAsync(_newStudioId, QuotaType.Artists, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new PlanLimitExceededException("Studio is at capacity.")));

        Func<Task> act = () => CreateSut().Handle(new AcceptStudioJoinInviteCommand(inviteId), default);

        await act.Should().ThrowAsync<PlanLimitExceededException>();
        _db.Artists.Should().BeEmpty();
        _db.StudioJoinInvites.Single(i => i.Id == inviteId).Status.Should().Be(StudioJoinInviteStatus.Pending);
    }

    [Fact]
    public async Task Handle_ExpiredInvite_ThrowsNotFoundException()
    {
        Guid inviteId = await SeedPendingInviteAndSoloStudio(expiresAt: DateTime.UtcNow.AddDays(-1));

        Func<Task> act = () => CreateSut().Handle(new AcceptStudioJoinInviteCommand(inviteId), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_UnknownInviteId_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new AcceptStudioJoinInviteCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_InvitingStudioInactive_ThrowsBusinessRuleViolationException()
    {
        Guid inviteId = await SeedPendingInviteAndSoloStudio(newStudioActive: false);

        Func<Task> act = () => CreateSut().Handle(new AcceptStudioJoinInviteCommand(inviteId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
        // The caller's own solo studio must not be touched when the inviting studio turns out
        // to be unusable — otherwise they'd be locked out of both.
        _db.Studios.Single(s => s.Id == _oldStudioId).IsActive.Should().BeTrue();
        _db.Artists.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_HappyPath_InvalidatesUsageCacheForNewStudioNotOldStudio()
    {
        Guid inviteId = await SeedPendingInviteAndSoloStudio();

        await CreateSut().Handle(new AcceptStudioJoinInviteCommand(inviteId), default);

        await _planLimits.Received(1)
            .InvalidateUsageCacheAsync(_newStudioId, QuotaType.Artists, Arg.Any<CancellationToken>());
        await _planLimits.DidNotReceive()
            .InvalidateUsageCacheAsync(_oldStudioId, QuotaType.Artists, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_IdentityFailureAfterQuotaCheck_LeavesInvitePendingAndNoArtistCreated()
    {
        // Proves the DB write is retry-safe: if token issuance fails, nothing has been
        // committed to the DB yet, so the invite is still Pending and a retry is possible —
        // unlike marking the invite Accepted before the Identity swap, which would permanently
        // block any retry.
        Guid inviteId = await SeedPendingInviteAndSoloStudio();
        _identity.IssueTokensForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((false, (string?)null, (string?)null, "Transient failure."));

        Func<Task> act = () => CreateSut().Handle(new AcceptStudioJoinInviteCommand(inviteId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
        _db.StudioJoinInvites.Single(i => i.Id == inviteId).Status.Should().Be(StudioJoinInviteStatus.Pending);
        _db.Artists.Should().BeEmpty();
        _db.Studios.Single(s => s.Id == _oldStudioId).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_RetryAfterIdentityFailure_SucceedsOnSecondAttempt()
    {
        Guid inviteId = await SeedPendingInviteAndSoloStudio();
        _identity.IssueTokensForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((false, (string?)null, (string?)null, "Transient failure."));

        Func<Task> firstAttempt = () => CreateSut().Handle(new AcceptStudioJoinInviteCommand(inviteId), default);
        await firstAttempt.Should().ThrowAsync<BusinessRuleViolationException>();

        // Identity now recovers — the idempotent Identity calls from the first attempt (role
        // swap, claim add/remove) are safely re-applied, then the DB write completes.
        _identity.IssueTokensForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((true, "access-token", "refresh-token", (string?)null));

        AuthResponse result = await CreateSut().Handle(new AcceptStudioJoinInviteCommand(inviteId), default);

        result.AccessToken.Should().Be("access-token");
        _db.StudioJoinInvites.Single(i => i.Id == inviteId).Status.Should().Be(StudioJoinInviteStatus.Accepted);
        _db.Artists.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_CallerNoLongerOwnsASoloStudio_ThrowsBusinessRuleViolationException()
    {
        Guid inviteId = await SeedPendingInviteAndSoloStudio(soloStudioStillOwned: false);

        Func<Task> act = () => CreateSut().Handle(new AcceptStudioJoinInviteCommand(inviteId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_DoubleAccept_SecondCallThrowsNotFoundException()
    {
        Guid inviteId = await SeedPendingInviteAndSoloStudio();
        await CreateSut().Handle(new AcceptStudioJoinInviteCommand(inviteId), default);

        // Second accept attempt: the invite is no longer Pending, and the caller no longer owns
        // an active solo studio (it was closed by the first accept) — either check alone would
        // already reject this, proving the flow is not re-enterable via a double-click/race.
        Func<Task> act = () => CreateSut().Handle(new AcceptStudioJoinInviteCommand(inviteId), default);

        await act.Should().ThrowAsync<DomainException>();
        _db.Artists.Should().HaveCount(1);
    }
}
