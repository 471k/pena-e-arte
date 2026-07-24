using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Infrastructure.Jobs;

public class SendArtistInviteJob(
    IIdentityService               identity,
    INotificationService           notifications,
    IEmailRenderer                 emailRenderer,
    IAppSettings                   appSettings,
    AppDbContext                   db,
    ILogger<SendArtistInviteJob>   logger)
{
    public async Task SendAsync(string email, string firstName, Guid studioId, CancellationToken ct = default)
    {
        (_, string? token, _) = await identity.GeneratePasswordResetTokenAsync(email);
        if (token is null)
        {
            logger.LogWarning("Could not generate invite token for {Email} — user may not exist", email);
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

        string html = emailRenderer.RenderArtistInvite(firstName, studioName, setPasswordUrl);

        try
        {
            await notifications.SendEmailAsync(email, $"You've been invited to {studioName}", html, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send artist invite email to {Email}", email);
        }
    }
}
