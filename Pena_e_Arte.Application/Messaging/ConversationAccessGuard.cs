using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Messaging;

/// <summary>
/// Shared by every handler that loads a single Conversation by id and must enforce
/// Conversation.IsParticipant — mirrors FeedbackAccessGuard's centralization reasoning.
/// </summary>
internal static class ConversationAccessGuard
{
    public static async Task<Conversation> LoadParticipantConversationAsync(
        IAppDbContext db, Guid conversationId, Guid userId, CancellationToken ct)
    {
        Conversation conversation = await db.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new NotFoundException(nameof(Conversation), conversationId);

        if (!conversation.IsParticipant(userId))
            throw new ForbiddenException("You do not have access to this conversation.");

        return conversation;
    }
}
