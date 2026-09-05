using FluentAssertions;
using Pena_e_Arte.Application.Studios.StudioJoinInvites;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios.StudioJoinInvites;

public class DeclineStudioJoinInviteHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly FakeCurrentUser _currentUser = new(Guid.NewGuid(), "owner", "jane@example.com");
    private readonly Guid _studioId = Guid.NewGuid();

    private DeclineStudioJoinInviteHandler CreateSut() => new(_db, _currentUser);

    private async Task<Guid> SeedPendingInvite(string email = "jane@example.com")
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = "Ink Collective", Slug = "ink-collective", City = "Lisbon" });
        StudioJoinInvite invite = new()
        {
            StudioId = _studioId,
            InvitedEmail = email,
            FirstName = "Jane",
            LastName = "Doe",
            Status = StudioJoinInviteStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
        };
        _db.StudioJoinInvites.Add(invite);
        await _db.SaveChangesAsync();
        return invite.Id;
    }

    [Fact]
    public async Task Handle_PendingInviteAddressedToCaller_MarksDeclined()
    {
        Guid inviteId = await SeedPendingInvite();

        await CreateSut().Handle(new DeclineStudioJoinInviteCommand(inviteId), default);

        _db.StudioJoinInvites.Single(i => i.Id == inviteId).Status.Should().Be(StudioJoinInviteStatus.Declined);
    }

    [Fact]
    public async Task Handle_InviteAddressedToSomeoneElse_ThrowsNotFoundException()
    {
        Guid inviteId = await SeedPendingInvite(email: "someone-else@example.com");

        Func<Task> act = () => CreateSut().Handle(new DeclineStudioJoinInviteCommand(inviteId), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_UnknownInviteId_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new DeclineStudioJoinInviteCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_AlreadyRespondedInvite_ThrowsNotFoundException()
    {
        Guid inviteId = await SeedPendingInvite();
        await CreateSut().Handle(new DeclineStudioJoinInviteCommand(inviteId), default);

        Func<Task> act = () => CreateSut().Handle(new DeclineStudioJoinInviteCommand(inviteId), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
