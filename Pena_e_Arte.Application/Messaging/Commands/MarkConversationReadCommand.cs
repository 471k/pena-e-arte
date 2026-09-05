using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Messaging.Commands;

public record MarkConversationReadCommand(Guid ConversationId) : IRequest;

public class MarkConversationReadHandler(IAppDbContext db, ICurrentUser user, IRealtimeNotifier realtime)
    : IRequestHandler<MarkConversationReadCommand>
{
    public async Task Handle(MarkConversationReadCommand command, CancellationToken ct)
    {
        Conversation conversation = await ConversationAccessGuard.LoadParticipantConversationAsync(
            db, command.ConversationId, user.UserId, ct);

        List<ChatMessage> unread = await db.ChatMessages.Where(m =>
            m.ConversationId == conversation.Id && m.SenderUserId != user.UserId && m.ReadAt == null)
            .ToListAsync(ct);
        if (unread.Count == 0) return;

        foreach (ChatMessage m in unread) m.MarkRead();
        await db.SaveChangesAsync(ct);

        (Guid otherId, _) = conversation.OtherParticipant(user.UserId);
        await realtime.NotifyUserAsync(otherId, "ConversationRead",
            new { conversation.Id, ReadByUserId = user.UserId }, ct);
    }
}
