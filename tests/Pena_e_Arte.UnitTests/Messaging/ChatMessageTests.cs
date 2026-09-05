using FluentAssertions;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.UnitTests.Messaging;

public class ChatMessageTests
{
    [Fact]
    public void MarkRead_IsIdempotent_SecondCallDoesNotChangeReadAt()
    {
        ChatMessage message = ChatMessage.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "client", "Hello there");

        message.MarkRead();
        DateTime? firstReadAt = message.ReadAt;

        message.MarkRead();

        message.ReadAt.Should().Be(firstReadAt);
    }

    [Fact]
    public void Create_TrimsBody()
    {
        ChatMessage message = ChatMessage.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "artist", "  Hi there  ");

        message.Body.Should().Be("Hi there");
    }
}
