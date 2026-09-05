using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Messaging.Commands;

public record SendChatMessageCommand(Guid ConversationId, SendChatMessageRequest Request)
    : IRequest<ChatMessageResponse>;

public class SendChatMessageHandler(
    IAppDbContext db, ICurrentUser user, ICurrentTenant tenant,
    IRealtimeNotifier realtime, IJobScheduler jobs)
    : IRequestHandler<SendChatMessageCommand, ChatMessageResponse>
{
    public async Task<ChatMessageResponse> Handle(SendChatMessageCommand command, CancellationToken ct)
    {
        Conversation conversation = await ConversationAccessGuard.LoadParticipantConversationAsync(
            db, command.ConversationId, user.UserId, ct);

        ChatMessage message = ChatMessage.Create(
            tenant.StudioId, conversation.Id, user.UserId, user.Role, command.Request.Body);
        db.ChatMessages.Add(message);
        conversation.RecordLastMessage(user.UserId, message.Body);

        await db.SaveChangesAsync(ct);

        ChatMessageResponse response = Map(message);

        (Guid recipientId, _) = conversation.OtherParticipant(user.UserId);
        await realtime.NotifyUserAsync(recipientId, "MessageReceived", response, ct);
        await realtime.NotifyUserAsync(user.UserId, "MessageReceived", response, ct);

        // Decision 6: only the first unread message in a streak triggers the email. Checked
        // AFTER inserting (not before, and not via a separate claimed/pending flag) by asking
        // "of every currently-unread message from this sender, is this one the earliest?" —
        // whichever message in a streak is earliest is definitionally the one that started
        // it, so exactly one message per streak answers yes under normal (non-concurrent)
        // sends. This is intentionally a plain read, not an atomic claim: an earlier version
        // used an EF Core concurrency token to make the claim itself atomic across concurrent
        // sends, but that made a *different* concurrent request's own unrelated message
        // insert fail too (a concurrency token's original value is checked on every update to
        // that row, not just updates that touch the token) — a strictly worse failure mode
        // than the rare double-email this was meant to prevent. A pathological interleaving of
        // two truly simultaneous sends in the same streak can still under-count or double-count
        // here; accepted as a UX nicety's residual risk, not a delivery guarantee — see
        // architecture.md's Decisions Log.
        Guid? earliestUnreadFromSenderId = await db.ChatMessages
            .Where(m => m.ConversationId == conversation.Id && m.SenderUserId == user.UserId && m.ReadAt == null)
            .OrderBy(m => m.CreatedAt)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(ct);

        if (earliestUnreadFromSenderId == message.Id) jobs.EnqueueNewMessageEmail(message.Id);

        return response;
    }

    internal static ChatMessageResponse Map(ChatMessage m) =>
        new(m.Id, m.ConversationId, m.SenderUserId, m.SenderRole, m.Body, m.CreatedAt, m.ReadAt);
}
