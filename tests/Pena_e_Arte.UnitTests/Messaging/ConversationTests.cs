using FluentAssertions;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.UnitTests.Messaging;

public class ConversationTests
{
    [Fact]
    public void Create_NormalizesParticipantOrder_RegardlessOfInputOrder()
    {
        Guid studioId = Guid.NewGuid();
        Guid smaller = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid larger = Guid.Parse("00000000-0000-0000-0000-000000000002");

        Conversation fromSmallFirst = Conversation.Create(studioId, smaller, "client", larger, "artist");
        Conversation fromLargeFirst = Conversation.Create(studioId, larger, "artist", smaller, "client");

        fromSmallFirst.ParticipantAUserId.Should().Be(smaller);
        fromSmallFirst.ParticipantBUserId.Should().Be(larger);
        fromLargeFirst.ParticipantAUserId.Should().Be(smaller);
        fromLargeFirst.ParticipantBUserId.Should().Be(larger);
        fromLargeFirst.ParticipantARole.Should().Be("client");
        fromLargeFirst.ParticipantBRole.Should().Be("artist");
    }

    [Fact]
    public void IsParticipant_ReturnsFalseForNonParticipant()
    {
        Guid userA = Guid.NewGuid();
        Guid userB = Guid.NewGuid();
        Conversation conversation = Conversation.Create(Guid.NewGuid(), userA, "client", userB, "artist");

        conversation.IsParticipant(userA).Should().BeTrue();
        conversation.IsParticipant(userB).Should().BeTrue();
        conversation.IsParticipant(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void RecordLastMessage_TruncatesLongBody()
    {
        Guid sender = Guid.NewGuid();
        Conversation conversation = Conversation.Create(Guid.NewGuid(), sender, "client", Guid.NewGuid(), "artist");
        string longBody = new string('x', 200);

        conversation.RecordLastMessage(sender, longBody);

        conversation.LastMessagePreview.Should().HaveLength(140);
        conversation.LastMessageSenderUserId.Should().Be(sender);
        conversation.LastMessageAt.Should().NotBeNull();
    }

    [Fact]
    public void OtherParticipant_ReturnsTheOtherUserAndRole()
    {
        Guid userA = Guid.NewGuid();
        Guid userB = Guid.NewGuid();
        Conversation conversation = Conversation.Create(Guid.NewGuid(), userA, "client", userB, "artist");

        (Guid otherId, string otherRole) = conversation.OtherParticipant(userA);

        otherId.Should().Be(userB);
        otherRole.Should().Be("artist");
    }
}
