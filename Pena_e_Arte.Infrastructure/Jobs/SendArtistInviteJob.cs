using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Infrastructure.Jobs;

public class SendArtistInviteJob(
    IIdentityService identity,
    INotificationService notifications,
    IEmailRenderer emailRenderer,
    IAppSettings appSettings,
    AppDbContext db,
    ILogger<SendArtistInviteJob> logger)
{
    public async Task SendAsync(string email, string firstName, Guid studioId, bool isRejoiningArtist = false, CancellationToken ct = default)
    {
        (_, string? token, _) = await identity.GeneratePasswordResetTokenAsync(email);
        if (token is null)
        {
            // Studio id only: the address is PII (CLAUDE.md rule 3), and this line logged it until 17 Nov 2026.
            logger.LogWarning("SendArtistInviteJob {StudioId}: skipped, could not generate an invite token (user may not exist)", studioId);
            return;
        }

        string studioName = await db.Studios
            .IgnoreQueryFilters()
            .Where(s => s.Id == studioId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct) ?? "your studio";

        string setPasswordUrl = $"{appSettings.BaseUrl}/reset-password" +
            $"?email={Uri.EscapeDataString(email)}" +
            $"&token={Uri.EscapeDataString(token)}";

        string html = emailRenderer.RenderArtistInvite(firstName, studioName, setPasswordUrl, isRejoiningArtist);

        string subject = isRejoiningArtist
            ? $"You've been added to {studioName}"
            : $"You've been invited to {studioName}";

        // Let send failures propagate so Hangfire's automatic-retry policy kicks in —
        // swallowing them here made every failed invite look "succeeded" in the dashboard.
        await notifications.SendEmailAsync(email, subject, html, ct);
        logger.LogInformation("SendArtistInviteJob {StudioId}: invite sent (rejoining: {IsRejoining})", studioId, isRejoiningArtist);
    }
}
