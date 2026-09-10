using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Campaigns;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Fan-out for one campaign send — enqueued once per SendCampaignCommand call (not a
/// recurring/cron job). Re-resolves the audience at send time (not the RecipientCount
/// snapshot SendCampaignHandler wrote) so a client who opts out between the send click and
/// this job actually running is never emailed. Every email carries a per-client signed
/// unsubscribe link (IMarketingOptOutSigner) — clicking it flips Client.MarketingOptIn to
/// false via WithdrawMarketingOptInCommand.
///
/// Verified against NotificationService/EmailRenderer: this codebase's Resend integration
/// has no client-side throttle of its own (one EmailSendAsync call per SendEmailAsync, no
/// batching wrapper) — the small per-recipient delay below is this job's own pacing, not a
/// pre-existing primitive being reused.
/// </summary>
public class SendCampaignJob(
    IAppDbContext db,
    INotificationService notifications,
    IMarketingOptOutSigner unsubscribeSigner,
    ILogger<SendCampaignJob> logger)
{
    private static readonly TimeSpan ThrottleDelay = TimeSpan.FromMilliseconds(200);

    public async Task RunAsync(Guid campaignId, CancellationToken ct = default)
    {
        // IgnoreQueryFilters(): Hangfire job, no ambient tenant scope.
        Campaign? campaign = await db.Campaigns.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct);

        if (campaign is null)
        {
            logger.LogWarning("SendCampaignJob: campaign {@CampaignId} not found, skipping", campaignId);
            return;
        }

        try
        {
            List<Client> audience = await db.ResolveCampaignAudienceAsync(campaign, ct);
            campaign.RecipientCount = audience.Count;

            int delivered = 0;
            foreach (Client client in audience)
            {
                try
                {
                    string unsubscribeToken = unsubscribeSigner.Sign(client.Id);
                    string unsubscribeUrl = $"https://tattooos.co/unsubscribe?token={unsubscribeToken}";
                    string body = campaign.BodyHtml +
                        $"""<p style="margin-top:32px;font-size:12px;color:#888;">""" +
                        $"""<a href="{unsubscribeUrl}">Unsubscribe from marketing emails</a></p>""";

                    await notifications.SendEmailAsync(client.Email, campaign.Subject, body, ct);
                    delivered++;
                }
                catch (Exception ex)
                {
                    // One recipient's failure must not block the rest of the send.
                    logger.LogWarning(ex,
                        "SendCampaignJob: failed to send campaign {@CampaignId} to client {@ClientId}",
                        campaignId, client.Id);
                }

                await Task.Delay(ThrottleDelay, ct);
            }

            campaign.DeliveredCount = delivered;
            campaign.Status = CampaignStatus.Sent;
            campaign.SentAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            campaign.Status = CampaignStatus.Failed;
            logger.LogError(ex, "SendCampaignJob: campaign {@CampaignId} send run failed", campaignId);
        }

        await db.SaveChangesAsync(ct);
    }
}
