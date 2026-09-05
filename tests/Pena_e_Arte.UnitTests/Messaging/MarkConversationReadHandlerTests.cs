using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Messaging.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Messaging;

public class MarkConversationReadHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    public MarkConversationReadHandlerTests()
    {
        _user.UserId.Returns(_userA);
    }

    private MarkConversationReadHandler CreateSut() => new(_db, _user, _realtime);

    [Fact]
    public async Task Handle_MarksOnlyOtherParticipantsUnreadMessages_NotCallersOwnSentMessages()
    {
        Conversation conversation = Conversation.Create(_studioId, _userA, "client", _userB, "artist");
        _db.Conversations.Add(conversation);
        ChatMessage fromCaller = ChatMessage.Create(_studioId, conversation.Id, _userA, "client", "mine");
        ChatMessage fromOther = ChatMessage.Create(_studioId, conversation.Id, _userB, "artist", "theirs");
        _db.ChatMessages.AddRange(fromCaller, fromOther);
        await _db.SaveChangesAsync();

        await CreateSut().Handle(new MarkConversationReadCommand(conversation.Id), default);

        _db.ChatMessages.Single(m => m.Id == fromCaller.Id).ReadAt.Should().BeNull();
        _db.ChatMessages.Single(m => m.Id == fromOther.Id).ReadAt.Should().NotBeNull();
    }
}
