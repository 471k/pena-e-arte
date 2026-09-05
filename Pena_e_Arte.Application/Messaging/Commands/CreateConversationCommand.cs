using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Messaging.Commands;

public record CreateConversationCommand(CreateConversationRequest Request) : IRequest<ConversationResponse>;

public class CreateConversationHandler(
    IAppDbContext db, ICurrentUser user, ICurrentTenant tenant, IIdentityService identity)
    : IRequestHandler<CreateConversationCommand, ConversationResponse>
{
    public async Task<ConversationResponse> Handle(CreateConversationCommand command, CancellationToken ct)
    {
        Guid recipientId = command.Request.RecipientUserId;

        List<EligibleContact> contacts = await ConversationEligibility.GetContactsAsync(
            db, identity, tenant.StudioId, user.UserId, user.Role, ct);
        EligibleContact? recipient = contacts.FirstOrDefault(c => c.UserId == recipientId);
        if (recipient is null)
            throw new ForbiddenException("You cannot start a conversation with this person.");

        Conversation? existing = await db.Conversations.FirstOrDefaultAsync(c =>
            (c.ParticipantAUserId == user.UserId && c.ParticipantBUserId == recipientId) ||
            (c.ParticipantAUserId == recipientId && c.ParticipantBUserId == user.UserId), ct);

        if (existing is not null) return await MapAsync(db, identity, existing, user.UserId, ct);

        Conversation conversation = Conversation.Create(
            tenant.StudioId, user.UserId, user.Role, recipientId, recipient.Role);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct);

        return await MapAsync(db, identity, conversation, user.UserId, ct);
    }

    internal static async Task<ConversationResponse> MapAsync(
        IAppDbContext db, IIdentityService identity, Conversation c, Guid callerUserId, CancellationToken ct)
    {
        (Guid otherId, string otherRole) = c.OtherParticipant(callerUserId);
        (string displayName, string? avatarUrl) = await ResolveDisplayAsync(db, identity, otherId, otherRole, ct);
        int unread = await db.ChatMessages.CountAsync(m =>
            m.ConversationId == c.Id && m.SenderUserId != callerUserId && m.ReadAt == null, ct);

        return new ConversationResponse(
            c.Id, otherId, otherRole, displayName, avatarUrl,
            c.LastMessageAt, c.LastMessagePreview,
            c.LastMessageSenderUserId == callerUserId, unread, c.CreatedAt);
    }

    /// <summary>Fallback shown when the other participant's row can't be resolved (e.g. deleted
    /// between the conversation being created and this read) — mirrors ResolveDisplayAsync's
    /// own per-role fallback strings below.</summary>
    internal static string DefaultDisplayName(string role) => role switch
    {
        "client" => "Client",
        "artist" => "Artist",
        _ => "Studio Owner",
    };

    internal static async Task<(string DisplayName, string? AvatarUrl)> ResolveDisplayAsync(
        IAppDbContext db, IIdentityService identity, Guid userId, string role, CancellationToken ct)
    {
        if (string.Equals(role, "client", StringComparison.OrdinalIgnoreCase))
        {
            Client? c = await db.Clients.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            return c is null ? ("Client", null) : ($"{c.FirstName} {c.LastName}", null);
        }
        if (string.Equals(role, "artist", StringComparison.OrdinalIgnoreCase))
        {
            Artist? a = await db.Artists.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            return a is null ? ("Artist", null) : ($"{a.FirstName} {a.LastName}", a.AvatarUrl);
        }
        // owner
        string? email = await identity.GetUserEmailAsync(userId, ct);
        string? name = email is null ? null : await identity.GetUserDisplayNameAsync(email, ct);
        return (name ?? "Studio Owner", null);
    }
}
