using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Messaging.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Messaging.Queries;

public record GetConversationMessagesQuery(Guid ConversationId, Guid? Before, int Take)
    : IRequest<List<ChatMessageResponse>>;

public class GetConversationMessagesHandler(IAppDbContext db, ICurrentUser user)
    : IRequestHandler<GetConversationMessagesQuery, List<ChatMessageResponse>>
{
    public async Task<List<ChatMessageResponse>> Handle(GetConversationMessagesQuery query, CancellationToken ct)
    {
        await ConversationAccessGuard.LoadParticipantConversationAsync(db, query.ConversationId, user.UserId, ct);

        int take = Math.Clamp(query.Take <= 0 ? 30 : query.Take, 1, 100);
        IQueryable<ChatMessage> q = db.ChatMessages.Where(m => m.ConversationId == query.ConversationId);

        if (query.Before is { } beforeId)
        {
            // Scoped to this conversation — without it, a cursor id borrowed from a
            // DIFFERENT conversation the caller has no access to would still resolve (its
            // row exists, just under another ConversationId), leaking that other
            // conversation's message timing/existence via the resulting page boundary.
            ChatMessage? cursor = await db.ChatMessages
                .FirstOrDefaultAsync(m => m.Id == beforeId && m.ConversationId == query.ConversationId, ct);
            if (cursor is not null) q = q.Where(m => m.CreatedAt < cursor.CreatedAt);
        }

        List<ChatMessage> page = await q.OrderByDescending(m => m.CreatedAt).Take(take).ToListAsync(ct);
        page.Reverse(); // return oldest-first within the page, newest page fetched first
        return page.Select(SendChatMessageHandler.Map).ToList();
    }
}
