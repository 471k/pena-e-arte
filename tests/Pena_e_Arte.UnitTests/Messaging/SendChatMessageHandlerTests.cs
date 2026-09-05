using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Messaging.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Messaging;

public class SendChatMessageHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    public SendChatMessageHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _user.UserId.Returns(_userA);
        _user.Role.Returns("client");
    }

    private SendChatMessageHandler CreateSut() => new(_db, _user, _tenant, _realtime, _jobs);

    private Conversation SeedConversation()
    {
        Conversation conversation = Conversation.Create(_studioId, _userA, "client", _userB, "artist");
        _db.Conversations.Add(conversation);
        _db.SaveChangesAsync().GetAwaiter().GetResult();
        return conversation;
    }

    [Fact]
    public async Task Handle_NonParticipant_ThrowsForbidden()
    {
        Conversation conversation = SeedConversation();
        _user.UserId.Returns(Guid.NewGuid()); // not a participant

        Func<Task> act = async () => await CreateSut().Handle(
            new SendChatMessageCommand(conversation.Id, new SendChatMessageRequest("hi")), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Handle_ValidSend_UpdatesConversationLastMessageFields()
    {
        Conversation conversation = SeedConversation();

        await CreateSut().Handle(
            new SendChatMessageCommand(conversation.Id, new SendChatMessageRequest("Hello there")), default);

        Conversation updated = _db.Conversations.Single(c => c.Id == conversation.Id);
        updated.LastMessagePreview.Should().Be("Hello there");
        updated.LastMessageAt.Should().NotBeNull();
        updated.LastMessageSenderUserId.Should().Be(_userA);
    }

    [Fact]
    public async Task Handle_FirstUnreadMessageInStreak_EnqueuesEmailExactlyOnce()
    {
        Conversation conversation = SeedConversation();

        ChatMessageResponse response = await CreateSut().Handle(
            new SendChatMessageCommand(conversation.Id, new SendChatMessageRequest("First message")), default);

        _jobs.Received(1).EnqueueNewMessageEmail(response.Id);
    }

    [Fact]
    public async Task Handle_SecondMessageBeforeFirstIsRead_DoesNotEnqueueAnotherEmail()
    {
        Conversation conversation = SeedConversation();

        ChatMessageResponse first = await CreateSut().Handle(
            new SendChatMessageCommand(conversation.Id, new SendChatMessageRequest("First message")), default);
        // Force a real ordering gap — two rapid in-process inserts can otherwise land on the
        // same DateTime.UtcNow tick, which would make "earliest unread" genuinely ambiguous.
        BackdateMessage(first.Id, DateTime.UtcNow.AddMinutes(-1));
        _jobs.ClearReceivedCalls();

        await CreateSut().Handle(
            new SendChatMessageCommand(conversation.Id, new SendChatMessageRequest("Second message")), default);

        _jobs.DidNotReceive().EnqueueNewMessageEmail(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Handle_ThirdMessageStillBeforeAnyIsRead_StillDoesNotEnqueue()
    {
        Conversation conversation = SeedConversation();
        ChatMessageResponse first = await CreateSut().Handle(
            new SendChatMessageCommand(conversation.Id, new SendChatMessageRequest("First")), default);
        BackdateMessage(first.Id, DateTime.UtcNow.AddMinutes(-2));
        ChatMessageResponse second = await CreateSut().Handle(
            new SendChatMessageCommand(conversation.Id, new SendChatMessageRequest("Second")), default);
        BackdateMessage(second.Id, DateTime.UtcNow.AddMinutes(-1));
        _jobs.ClearReceivedCalls();

        await CreateSut().Handle(
            new SendChatMessageCommand(conversation.Id, new SendChatMessageRequest("Third")), default);

        _jobs.DidNotReceive().EnqueueNewMessageEmail(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Handle_MessageFromTheOtherParticipant_StartsANewStreak_EnqueuesAgain()
    {
        Conversation conversation = SeedConversation();
        ChatMessageResponse fromA = await CreateSut().Handle(
            new SendChatMessageCommand(conversation.Id, new SendChatMessageRequest("From A")), default);
        BackdateMessage(fromA.Id, DateTime.UtcNow.AddMinutes(-1));
        _jobs.ClearReceivedCalls();

        _user.UserId.Returns(_userB);
        _user.Role.Returns("artist");
        ChatMessageResponse response = await CreateSut().Handle(
            new SendChatMessageCommand(conversation.Id, new SendChatMessageRequest("From B")), default);

        // A different sender's streak is independent of A's already-unread one.
        _jobs.Received(1).EnqueueNewMessageEmail(response.Id);
    }

    [Fact]
    public async Task Handle_MessageAfterPriorStreakWasRead_StartsANewStreak_EnqueuesAgain()
    {
        Conversation conversation = SeedConversation();
        ChatMessageResponse first = await CreateSut().Handle(
            new SendChatMessageCommand(conversation.Id, new SendChatMessageRequest("First message")), default);
        BackdateMessage(first.Id, DateTime.UtcNow.AddMinutes(-1));
        _db.ChatMessages.Single(m => m.Id == first.Id).MarkRead();
        await _db.SaveChangesAsync();
        _jobs.ClearReceivedCalls();

        ChatMessageResponse response = await CreateSut().Handle(
            new SendChatMessageCommand(conversation.Id, new SendChatMessageRequest("New streak")), default);

        _jobs.Received(1).EnqueueNewMessageEmail(response.Id);
    }

    private void BackdateMessage(Guid messageId, DateTime createdAt)
    {
        ChatMessage message = _db.ChatMessages.Single(m => m.Id == messageId);
        typeof(ChatMessage).GetProperty("CreatedAt")!.SetValue(message, createdAt);
        _db.SaveChangesAsync().GetAwaiter().GetResult();
    }
}
