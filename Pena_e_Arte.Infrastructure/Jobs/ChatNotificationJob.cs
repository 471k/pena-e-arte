using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Infrastructure.Jobs;

public class ChatNotificationJob(
    AppDbContext db,
    INotificationPreferenceService prefs,
    INotificationService notifications,
    IIdentityService identity,
    IAppSettings appSettings,
    ILogger<ChatNotificationJob> logger)
{
    public async Task SendNewMessageEmailAsync(Guid chatMessageId, CancellationToken ct = default)
    {
        // IgnoreQueryFilters is used here — not because of an admin cross-tenant read (this
        // feature adds none, see Decision 2), but because this job runs with no ICurrentTenant
        // scope at all (Hangfire jobs are not HTTP requests) — the tenant filter's underlying
        // predicate would throw for a null/default tenant otherwise. Same pattern as
        // ManualReminderJob/SendArtistInviteJob's own IgnoreQueryFilters() usage.
        ChatMessage? message = await db.ChatMessages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == chatMessageId && m.DeletedAt == null, ct);
        if (message is null)
        {
            logger.LogWarning("ChatMessage {@ChatMessageId} not found for new-message email job", chatMessageId);
            return;
        }

        Conversation? conversation = await db.Conversations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == message.ConversationId && c.DeletedAt == null, ct);
        if (conversation is null) return;

        (Guid recipientUserId, _) = conversation.OtherParticipant(message.SenderUserId);

        bool enabled = await prefs.IsEnabledAsync(
            message.StudioId, NotificationType.MessageReceived, NotificationChannel.Email, ct);
        if (!enabled) return;

        string? email = await identity.GetUserEmailAsync(recipientUserId, ct);
        if (string.IsNullOrEmpty(email)) return;

        string body =
            $"<p>You have a new message waiting for you.</p>" +
            $"<p><a href=\"{appSettings.BaseUrl}/messages\">Log in to reply</a></p>";

        await notifications.SendEmailAsync(email, "You have a new message", body, ct);
    }
}
