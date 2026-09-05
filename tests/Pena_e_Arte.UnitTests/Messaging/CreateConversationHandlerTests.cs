using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.Messaging.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Messaging;

public class CreateConversationHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly Guid _studioId = Guid.NewGuid();

    public CreateConversationHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _tenant.IsSet.Returns(true);
    }

    private CreateConversationHandler CreateSut() => new(_db, _user, _tenant, _identity);

    [Fact]
    public async Task Handle_EligibleParties_CreatesConversation()
    {
        Guid clientUserId = Guid.NewGuid();
        Guid artistUserId = Guid.NewGuid();
        Artist artist = SeedArtist(artistUserId);
        SeedClient(clientUserId, artist.Id);
        _user.UserId.Returns(clientUserId);
        _user.Role.Returns("client");

        ConversationResponse result = await CreateSut().Handle(
            new CreateConversationCommand(new CreateConversationRequest(artistUserId)), default);

        result.OtherUserId.Should().Be(artistUserId);
        result.OtherRole.Should().Be("artist");
        (await _db.Conversations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Handle_CalledTwiceForSamePair_IsIdempotent_ReturnsSameConversation()
    {
        Guid clientUserId = Guid.NewGuid();
        Guid artistUserId = Guid.NewGuid();
        Artist artist = SeedArtist(artistUserId);
        SeedClient(clientUserId, artist.Id);
        _user.UserId.Returns(clientUserId);
        _user.Role.Returns("client");

        ConversationResponse first = await CreateSut().Handle(
            new CreateConversationCommand(new CreateConversationRequest(artistUserId)), default);
        ConversationResponse second = await CreateSut().Handle(
            new CreateConversationCommand(new CreateConversationRequest(artistUserId)), default);

        second.Id.Should().Be(first.Id);
        (await _db.Conversations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Handle_IneligibleParties_ThrowsForbidden()
    {
        Guid clientUserId = Guid.NewGuid();
        Guid unrelatedArtistUserId = Guid.NewGuid();
        SeedArtist(unrelatedArtistUserId);
        SeedClient(clientUserId, assignedArtistId: null);
        _user.UserId.Returns(clientUserId);
        _user.Role.Returns("client");

        Func<Task> act = async () => await CreateSut().Handle(
            new CreateConversationCommand(new CreateConversationRequest(unrelatedArtistUserId)), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Handle_IssuerCaller_ThrowsForbidden()
    {
        Guid artistUserId = Guid.NewGuid();
        SeedArtist(artistUserId);
        _user.UserId.Returns(Guid.NewGuid());
        _user.Role.Returns("issuer");

        Func<Task> act = async () => await CreateSut().Handle(
            new CreateConversationCommand(new CreateConversationRequest(artistUserId)), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    private Artist SeedArtist(Guid userId)
    {
        Artist artist = new()
        {
            StudioId = _studioId,
            UserId = userId,
            FirstName = "Art",
            LastName = "Ist",
            Email = $"{Guid.NewGuid():N}@artist.test",
            IsActive = true,
        };
        _db.Artists.Add(artist);
        _db.SaveChangesAsync().GetAwaiter().GetResult();
        return artist;
    }

    private void SeedClient(Guid userId, Guid? assignedArtistId)
    {
        _db.Clients.Add(new Client
        {
            StudioId = _studioId,
            UserId = userId,
            FirstName = "Cli",
            LastName = "Ent",
            Email = $"{Guid.NewGuid():N}@client.test",
            ArtistId = assignedArtistId,
        });
        _db.SaveChangesAsync().GetAwaiter().GetResult();
    }
}
