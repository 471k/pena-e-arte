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
        string outcome = await SendAsync(chatMessageId, ct);
        logger.LogInformation("ChatNotificationJob {ChatMessageId}: {Outcome}", chatMessageId, outcome);
    }

    private async Task<string> SendAsync(Guid chatMessageId, CancellationToken ct)
    {
        // IgnoreQueryFilters is used here — not because of an admin cross-tenant read (this
        // feature adds none, see Decision 2), but because this job runs with no ICurrentTenant
        // scope at all (Hangfire jobs are not HTTP requests). With no tenant the filter doesn't
        // throw: it compares StudioId to Guid.Empty and silently matches nothing, so this read
        // would find no message and the job would quietly send nothing. Same pattern as
        // ManualReminderJob/SendArtistInviteJob's own IgnoreQueryFilters() usage.
        ChatMessage? message = await db.ChatMessages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == chatMessageId && m.DeletedAt == null, ct);
        if (message is null) return "skipped, message not found or deleted";

        Conversation? conversation = await db.Conversations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == message.ConversationId && c.DeletedAt == null, ct);
        if (conversation is null) return "skipped, conversation not found or deleted";

        (Guid recipientUserId, _) = conversation.OtherParticipant(message.SenderUserId);

        bool enabled = await prefs.IsEnabledAsync(
            message.StudioId, NotificationType.MessageReceived, NotificationChannel.Email, ct);
        if (!enabled) return "skipped, the studio turned message emails off";

        string? email = await identity.GetUserEmailAsync(recipientUserId, ct);
        if (string.IsNullOrEmpty(email)) return "skipped, recipient has no email";

        string body =
            $"<p>You have a new message waiting for you.</p>" +
            $"<p><a href=\"{appSettings.BaseUrl}/messages\">Log in to reply</a></p>";

        await notifications.SendEmailAsync(email, "You have a new message", body, ct);
        return "email sent";
    }
}
