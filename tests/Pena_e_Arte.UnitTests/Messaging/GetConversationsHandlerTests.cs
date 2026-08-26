using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Messaging.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Messaging;

public class GetConversationsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();

    public GetConversationsHandlerTests()
    {
        _user.UserId.Returns(_userA);
    }

    private GetConversationsHandler CreateSut() => new(_db, _user, _identity);

    [Fact]
    public async Task Handle_NoConversations_ReturnsEmptyList()
    {
        List<ConversationResponse> result = await CreateSut().Handle(new GetConversationsQuery(), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsOnlyTheCallersOwnConversations()
    {
        Guid artistUserId = SeedArtist();
        Conversation mine = Conversation.Create(_studioId, _userA, "client", artistUserId, "artist");
        Conversation notMine = Conversation.Create(_studioId, Guid.NewGuid(), "client", artistUserId, "artist");
        _db.Conversations.AddRange(mine, notMine);
        await _db.SaveChangesAsync();

        List<ConversationResponse> result = await CreateSut().Handle(new GetConversationsQuery(), default);

        result.Should().ContainSingle(c => c.Id == mine.Id);
    }

    [Fact]
    public async Task Handle_OrdersByLastMessageAtDescending_FallingBackToCreatedAt()
    {
        Guid artistUserId = SeedArtist();
        Conversation older = Conversation.Create(_studioId, _userA, "client", artistUserId, "artist");
        Conversation newer = Conversation.Create(_studioId, _userA, "client", Guid.NewGuid(), "artist");
        _db.Conversations.AddRange(older, newer);
        await _db.SaveChangesAsync();

        older.RecordLastMessage(_userA, "old message");
        newer.RecordLastMessage(_userA, "new message");
        typeof(Conversation).GetProperty(nameof(Conversation.LastMessageAt))!
            .SetValue(older, DateTime.UtcNow.AddDays(-1));
        await _db.SaveChangesAsync();

        List<ConversationResponse> result = await CreateSut().Handle(new GetConversationsQuery(), default);

        result.Select(c => c.Id).Should().ContainInOrder(newer.Id, older.Id);
    }

    [Fact]
    public async Task Handle_UnreadCount_CountsOnlyMessagesFromTheOtherParticipant()
    {
        Guid artistUserId = SeedArtist();
        Conversation conversation = Conversation.Create(_studioId, _userA, "client", artistUserId, "artist");
        _db.Conversations.Add(conversation);
        await _db.SaveChangesAsync();
        _db.ChatMessages.Add(ChatMessage.Create(_studioId, conversation.Id, artistUserId, "artist", "from them"));
        _db.ChatMessages.Add(ChatMessage.Create(_studioId, conversation.Id, _userA, "client", "from me"));
        await _db.SaveChangesAsync();

        List<ConversationResponse> result = await CreateSut().Handle(new GetConversationsQuery(), default);

        result.Single().UnreadCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ResolvesDisplayNameForEachRole()
    {
        Guid artistUserId = SeedArtist("Marco", "Ink");
        Conversation conversation = Conversation.Create(_studioId, _userA, "client", artistUserId, "artist");
        _db.Conversations.Add(conversation);
        await _db.SaveChangesAsync();

        List<ConversationResponse> result = await CreateSut().Handle(new GetConversationsQuery(), default);

        result.Single().OtherDisplayName.Should().Be("Marco Ink");
    }

    private Guid SeedArtist(string firstName = "Art", string lastName = "Ist")
    {
        Guid userId = Guid.NewGuid();
        _db.Artists.Add(new Artist
        {
            StudioId = _studioId,
            UserId = userId,
            FirstName = firstName,
            LastName = lastName,
            Email = $"{Guid.NewGuid():N}@artist.test",
        });
        _db.SaveChangesAsync().GetAwaiter().GetResult();
        return userId;
    }
}
