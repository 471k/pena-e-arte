using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Messaging.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Messaging.Queries;

public record GetConversationsQuery : IRequest<List<ConversationResponse>>;

public class GetConversationsHandler(IAppDbContext db, ICurrentUser user, IIdentityService identity)
    : IRequestHandler<GetConversationsQuery, List<ConversationResponse>>
{
    public async Task<List<ConversationResponse>> Handle(GetConversationsQuery query, CancellationToken ct)
    {
        List<Conversation> conversations = await db.Conversations
            .Where(c => c.ParticipantAUserId == user.UserId || c.ParticipantBUserId == user.UserId)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .ToListAsync(ct);

        if (conversations.Count == 0) return [];

        // Batched instead of one MapAsync() call per conversation (which was a display-name
        // lookup + an unread CountAsync per row — 2-3 round trips per conversation, ~60-90
        // for a 30-conversation inbox) — one grouped unread-count query and one batched
        // display-name lookup per role, regardless of how many conversations there are.
        List<Guid> conversationIds = conversations.Select(c => c.Id).ToList();
        Dictionary<Guid, int> unreadByConversation = await db.ChatMessages
            .Where(m => conversationIds.Contains(m.ConversationId) && m.SenderUserId != user.UserId && m.ReadAt == null)
            .GroupBy(m => m.ConversationId)
            .Select(g => new { ConversationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ConversationId, x => x.Count, ct);

        List<(Guid UserId, string Role)> others = [.. conversations
            .Select(c => c.OtherParticipant(user.UserId))
            .Distinct()];
        Dictionary<Guid, (string DisplayName, string? AvatarUrl)> displayByUserId =
            await ResolveDisplaysAsync(db, identity, others, ct);

        List<ConversationResponse> results = [];
        foreach (Conversation c in conversations)
        {
            (Guid otherId, string otherRole) = c.OtherParticipant(user.UserId);
            (string displayName, string? avatarUrl) = displayByUserId.TryGetValue(otherId, out var d)
                ? d
                : (CreateConversationHandler.DefaultDisplayName(otherRole), null);

            results.Add(new ConversationResponse(
                c.Id, otherId, otherRole, displayName, avatarUrl,
                c.LastMessageAt, c.LastMessagePreview,
                c.LastMessageSenderUserId == user.UserId,
                unreadByConversation.GetValueOrDefault(c.Id, 0), c.CreatedAt));
        }
        return results;
    }

    private static async Task<Dictionary<Guid, (string DisplayName, string? AvatarUrl)>> ResolveDisplaysAsync(
        IAppDbContext db, IIdentityService identity, List<(Guid UserId, string Role)> others, CancellationToken ct)
    {
        Dictionary<Guid, (string, string?)> result = [];

        List<Guid> clientUserIds = [.. others.Where(o => o.Role == "client").Select(o => o.UserId)];
        if (clientUserIds.Count > 0)
        {
            List<Client> clients = await db.Clients.Where(c => clientUserIds.Contains(c.UserId!.Value)).ToListAsync(ct);
            foreach (Client c in clients) result[c.UserId!.Value] = ($"{c.FirstName} {c.LastName}", null);
        }

        List<Guid> artistUserIds = [.. others.Where(o => o.Role == "artist").Select(o => o.UserId)];
        if (artistUserIds.Count > 0)
        {
            List<Artist> artists = await db.Artists.Where(a => artistUserIds.Contains(a.UserId!.Value)).ToListAsync(ct);
            foreach (Artist a in artists) result[a.UserId!.Value] = ($"{a.FirstName} {a.LastName}", a.AvatarUrl);
        }

        // At most one distinct owner per studio in practice, so this stays a single extra
        // round trip per request regardless of conversation count — not worth batching
        // against IIdentityService, which has no bulk-lookup API.
        foreach ((Guid userId, string role) in others.Where(o => o.Role == "owner"))
        {
            (string displayName, string? avatarUrl) =
                await CreateConversationHandler.ResolveDisplayAsync(db, identity, userId, role, ct);
            result[userId] = (displayName, avatarUrl);
        }

        return result;
    }
}
