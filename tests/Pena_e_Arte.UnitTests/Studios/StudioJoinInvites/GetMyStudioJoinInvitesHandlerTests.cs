using FluentAssertions;
using Pena_e_Arte.Application.Studios.StudioJoinInvites;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios.StudioJoinInvites;

public class GetMyStudioJoinInvitesHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly FakeCurrentUser _currentUser = new(Guid.NewGuid(), "owner", "jane@example.com");
    private readonly Guid _studioId = Guid.NewGuid();

    private GetMyStudioJoinInvitesHandler CreateSut() => new(_db, _currentUser);

    private async Task SeedStudio()
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = "Ink Collective", Slug = "ink-collective", City = "Lisbon" });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_PendingInviteAddressedToCaller_IsReturned()
    {
        await SeedStudio();
        _db.StudioJoinInvites.Add(new StudioJoinInvite
        {
            StudioId = _studioId,
            InvitedEmail = "jane@example.com",
            FirstName = "Jane",
            LastName = "Doe",
            Status = StudioJoinInviteStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
        });
        await _db.SaveChangesAsync();

        List<MyStudioJoinInviteResponse> result = await CreateSut().Handle(new GetMyStudioJoinInvitesQuery(), default);

        result.Should().ContainSingle();
        result[0].StudioName.Should().Be("Ink Collective");
    }

    [Fact]
    public async Task Handle_InviteAddressedToSomeoneElse_IsExcluded()
    {
        await SeedStudio();
        _db.StudioJoinInvites.Add(new StudioJoinInvite
        {
            StudioId = _studioId,
            InvitedEmail = "someone-else@example.com",
            FirstName = "Jane",
            LastName = "Doe",
            Status = StudioJoinInviteStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
        });
        await _db.SaveChangesAsync();

        List<MyStudioJoinInviteResponse> result = await CreateSut().Handle(new GetMyStudioJoinInvitesQuery(), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ExpiredInvite_IsExcluded()
    {
        await SeedStudio();
        _db.StudioJoinInvites.Add(new StudioJoinInvite
        {
            StudioId = _studioId,
            InvitedEmail = "jane@example.com",
            FirstName = "Jane",
            LastName = "Doe",
            Status = StudioJoinInviteStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
        });
        await _db.SaveChangesAsync();

        List<MyStudioJoinInviteResponse> result = await CreateSut().Handle(new GetMyStudioJoinInvitesQuery(), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_InviteFromInactiveStudio_IsExcluded()
    {
        _db.Studios.Add(new Studio
        {
            Id = _studioId,
            Name = "Ink Collective",
            Slug = "ink-collective",
            City = "Lisbon",
            IsActive = false,
        });
        _db.StudioJoinInvites.Add(new StudioJoinInvite
        {
            StudioId = _studioId,
            InvitedEmail = "jane@example.com",
            FirstName = "Jane",
            LastName = "Doe",
            Status = StudioJoinInviteStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
        });
        await _db.SaveChangesAsync();

        List<MyStudioJoinInviteResponse> result = await CreateSut().Handle(new GetMyStudioJoinInvitesQuery(), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AlreadyRespondedInvite_IsExcluded()
    {
        await SeedStudio();
        _db.StudioJoinInvites.Add(new StudioJoinInvite
        {
            StudioId = _studioId,
            InvitedEmail = "jane@example.com",
            FirstName = "Jane",
            LastName = "Doe",
            Status = StudioJoinInviteStatus.Accepted,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
        });
        await _db.SaveChangesAsync();

        List<MyStudioJoinInviteResponse> result = await CreateSut().Handle(new GetMyStudioJoinInvitesQuery(), default);

        result.Should().BeEmpty();
    }
}
