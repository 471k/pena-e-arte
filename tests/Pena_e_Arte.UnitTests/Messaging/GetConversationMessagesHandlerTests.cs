using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Messaging.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Messaging;

public class GetConversationMessagesHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    public GetConversationMessagesHandlerTests()
    {
        _user.UserId.Returns(_userA);
    }

    private GetConversationMessagesHandler CreateSut() => new(_db, _user);

    private Conversation SeedConversation()
    {
        Conversation conversation = Conversation.Create(_studioId, _userA, "client", _userB, "artist");
        _db.Conversations.Add(conversation);
        _db.SaveChangesAsync().GetAwaiter().GetResult();
        return conversation;
    }

    [Fact]
    public async Task Handle_ReturnsMessagesOldestFirstWithinPage()
    {
        Conversation conversation = SeedConversation();
        DateTime baseTime = DateTime.UtcNow.AddMinutes(-10);
        for (int i = 0; i < 5; i++)
        {
            ChatMessage m = ChatMessage.Create(_studioId, conversation.Id, _userA, "client", $"msg {i}");
            typeof(ChatMessage).GetProperty("CreatedAt")!.SetValue(m, baseTime.AddMinutes(i));
            _db.ChatMessages.Add(m);
        }
        await _db.SaveChangesAsync();

        List<ChatMessageResponse> result = await CreateSut().Handle(
            new GetConversationMessagesQuery(conversation.Id, null, 30), default);

        result.Should().HaveCount(5);
        result.Select(m => m.Body).Should().ContainInOrder("msg 0", "msg 1", "msg 2", "msg 3", "msg 4");
    }

    [Fact]
    public async Task Handle_BeforeCursorFromAnotherConversation_IsIgnored_DoesNotLeakItsTiming()
    {
        Conversation conversation = SeedConversation();
        _db.ChatMessages.Add(ChatMessage.Create(_studioId, conversation.Id, _userA, "client", "mine"));
        await _db.SaveChangesAsync();

        // A real message id, but it belongs to a conversation the caller is not a
        // participant of and never requested.
        Conversation otherConversation = Conversation.Create(_studioId, Guid.NewGuid(), "client", Guid.NewGuid(), "artist");
        _db.Conversations.Add(otherConversation);
        ChatMessage foreignCursor = ChatMessage.Create(_studioId, otherConversation.Id, Guid.NewGuid(), "client", "not mine");
        typeof(ChatMessage).GetProperty("CreatedAt")!.SetValue(foreignCursor, DateTime.UtcNow.AddDays(-30));
        _db.ChatMessages.Add(foreignCursor);
        await _db.SaveChangesAsync();

        List<ChatMessageResponse> result = await CreateSut().Handle(
            new GetConversationMessagesQuery(conversation.Id, foreignCursor.Id, 30), default);

        // If the cursor were resolved without scoping to `conversation`, its far-past
        // CreatedAt would filter out every one of this conversation's messages.
        result.Should().ContainSingle(m => m.Body == "mine");
    }

    [Fact]
    public async Task Handle_TakeIsClampedBetween1And100()
    {
        Conversation conversation = SeedConversation();
        _db.ChatMessages.Add(ChatMessage.Create(_studioId, conversation.Id, _userA, "client", "only one"));
        await _db.SaveChangesAsync();

        List<ChatMessageResponse> result = await CreateSut().Handle(
            new GetConversationMessagesQuery(conversation.Id, null, Take: 0), default);

        result.Should().HaveCount(1); // take <= 0 falls back to the default of 30
    }
}
