using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Messaging.Queries;

public record GetUnreadMessageCountQuery : IRequest<int>;

public class GetUnreadMessageCountHandler(IAppDbContext db, ICurrentUser user)
    : IRequestHandler<GetUnreadMessageCountQuery, int>
{
    public async Task<int> Handle(GetUnreadMessageCountQuery query, CancellationToken ct)
    {
        List<Guid> myConversationIds = await db.Conversations
            .Where(c => c.ParticipantAUserId == user.UserId || c.ParticipantBUserId == user.UserId)
            .Select(c => c.Id).ToListAsync(ct);

        return await db.ChatMessages.CountAsync(m =>
            myConversationIds.Contains(m.ConversationId) && m.SenderUserId != user.UserId && m.ReadAt == null, ct);
    }
}
