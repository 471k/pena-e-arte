using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Messaging.Queries;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Messaging;

public class GetUnreadMessageCountHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    public GetUnreadMessageCountHandlerTests()
    {
        _user.UserId.Returns(_userA);
    }

    private GetUnreadMessageCountHandler CreateSut() => new(_db, _user);

    private Conversation SeedConversation()
    {
        Conversation conversation = Conversation.Create(_studioId, _userA, "client", _userB, "artist");
        _db.Conversations.Add(conversation);
        _db.SaveChangesAsync().GetAwaiter().GetResult();
        return conversation;
    }

    [Fact]
    public async Task Handle_CountsOnlyMessagesFromOthers_NotTheCallersOwnSentMessages()
    {
        Conversation conversation = SeedConversation();
        _db.ChatMessages.Add(ChatMessage.Create(_studioId, conversation.Id, _userB, "artist", "from them"));
        _db.ChatMessages.Add(ChatMessage.Create(_studioId, conversation.Id, _userA, "client", "from me"));
        await _db.SaveChangesAsync();

        int result = await CreateSut().Handle(new GetUnreadMessageCountQuery(), default);

        result.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ExcludesAlreadyReadMessages()
    {
        Conversation conversation = SeedConversation();
        ChatMessage read = ChatMessage.Create(_studioId, conversation.Id, _userB, "artist", "read already");
        read.MarkRead();
        _db.ChatMessages.Add(read);
        _db.ChatMessages.Add(ChatMessage.Create(_studioId, conversation.Id, _userB, "artist", "still unread"));
        await _db.SaveChangesAsync();

        int result = await CreateSut().Handle(new GetUnreadMessageCountQuery(), default);

        result.Should().Be(1);
    }

    [Fact]
    public async Task Handle_SumsAcrossMultipleConversations()
    {
        Conversation conversationB = SeedConversation();
        Conversation conversationC = Conversation.Create(_studioId, _userA, "client", Guid.NewGuid(), "owner");
        _db.Conversations.Add(conversationC);
        await _db.SaveChangesAsync();

        _db.ChatMessages.Add(ChatMessage.Create(_studioId, conversationB.Id, _userB, "artist", "hi"));
        _db.ChatMessages.Add(ChatMessage.Create(_studioId, conversationC.Id, conversationC.OtherParticipant(_userA).UserId, "owner", "hey"));
        await _db.SaveChangesAsync();

        int result = await CreateSut().Handle(new GetUnreadMessageCountQuery(), default);

        result.Should().Be(2);
    }

    [Fact]
    public async Task Handle_NoConversations_ReturnsZero()
    {
        int result = await CreateSut().Handle(new GetUnreadMessageCountQuery(), default);

        result.Should().Be(0);
    }
}
