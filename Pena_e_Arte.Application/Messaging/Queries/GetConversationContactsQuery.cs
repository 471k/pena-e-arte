using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Messaging.Queries;

public record GetConversationContactsQuery : IRequest<List<ConversationContactResponse>>;

public class GetConversationContactsHandler(
    IAppDbContext db, ICurrentUser user, ICurrentTenant tenant, IIdentityService identity)
    : IRequestHandler<GetConversationContactsQuery, List<ConversationContactResponse>>
{
    public async Task<List<ConversationContactResponse>> Handle(GetConversationContactsQuery query, CancellationToken ct)
    {
        if (!tenant.IsSet) return [];

        List<EligibleContact> contacts = await ConversationEligibility.GetContactsAsync(
            db, identity, tenant.StudioId, user.UserId, user.Role, ct);

        // The caller's own conversations, loaded once, instead of one existing-conversation
        // query per eligible contact — an owner eligible to message every artist/client at a
        // 100+-person studio was issuing 100+ sequential queries just to open this dialog.
        List<Conversation> myConversations = await db.Conversations
            .Where(x => x.ParticipantAUserId == user.UserId || x.ParticipantBUserId == user.UserId)
            .ToListAsync(ct);
        Dictionary<Guid, Guid> conversationIdByOtherUserId = myConversations
            .ToDictionary(x => x.OtherParticipant(user.UserId).UserId, x => x.Id);

        List<ConversationContactResponse> results = [];
        foreach (EligibleContact c in contacts)
        {
            Guid? existingConversationId = conversationIdByOtherUserId.TryGetValue(c.UserId, out Guid id) ? id : null;
            results.Add(new ConversationContactResponse(c.UserId, c.Role, c.DisplayName, c.AvatarUrl, existingConversationId));
        }
        return results;
    }
}
