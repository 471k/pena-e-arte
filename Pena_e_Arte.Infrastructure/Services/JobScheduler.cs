using Hangfire;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using StackExchange.Redis;

namespace Pena_e_Arte.Infrastructure.Services;

public class JobScheduler(IBackgroundJobClient backgroundJobs, IConnectionMultiplexer redis, ILogger<JobScheduler> logger)
    : IJobScheduler
{
    /// <summary>
    /// C-5: POK sends each webhook 3 times within ~3 s (measured on staging, 17–18 Oct 2026), and every
    /// reconciliation run re-checks every in-flight card payment with POK — so one payment cost three
    /// full runs. The first trigger marks this window in Redis and schedules ONE run at its end;
    /// triggers inside the window are skipped. That is safe because the scheduled run starts after
    /// them (Hangfire never runs a scheduled job early), so it sees whatever they announced.
    /// </summary>
    public static readonly TimeSpan ReconciliationDebounce = TimeSpan.FromSeconds(10);

    public const string ReconciliationDebounceKey = "pok:reconcile:debounce";

    public string ScheduleAppointmentReminder(Guid appointmentId, string type, DateTimeOffset enqueueAt) =>
        backgroundJobs.Schedule<AppointmentReminderJob>(
            j => j.SendReminderAsync(appointmentId, type, default), enqueueAt);

    public void CancelAppointmentJobs(string? jobId48h, string? jobId24h)
    {
        if (!string.IsNullOrEmpty(jobId48h)) backgroundJobs.Delete(jobId48h);
        if (!string.IsNullOrEmpty(jobId24h)) backgroundJobs.Delete(jobId24h);
    }

    public void ScheduleTrialExpiryWarning(Guid studioId, DateTimeOffset enqueueAt) =>
        backgroundJobs.Schedule<TrialExpiryWarningJob>(
            j => j.ExecuteAsync(studioId, default), enqueueAt);

    public void ScheduleTrialExpiry(Guid studioId, DateTimeOffset enqueueAt) =>
        backgroundJobs.Schedule<TrialExpiryJob>(
            j => j.ExecuteAsync(studioId, default), enqueueAt);

    public void ScheduleGracePeriodEnd(Guid studioId, DateTimeOffset enqueueAt) =>
        backgroundJobs.Schedule<GracePeriodEndJob>(
            j => j.ExecuteAsync(studioId, default), enqueueAt);

    public void ScheduleDesignRevisionTimeout(Guid revisionId, DateTimeOffset enqueueAt) =>
        backgroundJobs.Schedule<DesignRevisionTimeoutJob>(
            j => j.ExecuteAsync(revisionId, default), enqueueAt);

    public void TriggerIndustryReportNow() =>
        backgroundJobs.Enqueue<IndustryReportJob>(j => j.RunAsync(CancellationToken.None));

    public void TriggerPaymentReconciliationNow()
    {
        bool firstInWindow;
        try
        {
            // All five arguments, typed exactly, so this binds to the (TimeSpan?, When, CommandFlags)
            // overload — StackExchange.Redis 3.x adds look-alike overloads (Expiration / ValueCondition).
            firstInWindow = redis.GetDatabase().StringSet(
                ReconciliationDebounceKey, "1", (TimeSpan?)ReconciliationDebounce, When.NotExists, CommandFlags.None);
        }
        catch (RedisException ex)
        {
            // Redis is a best-effort de-duplicator here, never a gate: without it, run at once as before.
            logger.LogWarning("Reconciliation debounce unavailable ({ErrorType}); running payment reconciliation now.", ex.GetType().Name);
            backgroundJobs.Enqueue<PaymentReconciliationJob>(j => j.RunAsync(CancellationToken.None));
            return;
        }

        if (firstInWindow)
            backgroundJobs.Schedule<PaymentReconciliationJob>(j => j.RunAsync(CancellationToken.None), ReconciliationDebounce);
    }

    public void EnqueueArtistInvite(string email, string firstName, Guid studioId, bool isRejoiningArtist = false) =>
        backgroundJobs.Enqueue<SendArtistInviteJob>(j => j.SendAsync(email, firstName, studioId, isRejoiningArtist, CancellationToken.None));

    public string ScheduleManualReminder(Guid manualReminderId, DateTimeOffset sendAt) =>
        sendAt <= DateTimeOffset.UtcNow
            ? backgroundJobs.Enqueue<ManualReminderJob>(j => j.SendAsync(manualReminderId, default))
            : backgroundJobs.Schedule<ManualReminderJob>(j => j.SendAsync(manualReminderId, default), sendAt);

    public void CancelJob(string jobId) => backgroundJobs.Delete(jobId);

    public void EnqueueNewMessageEmail(Guid chatMessageId) =>
        backgroundJobs.Enqueue<ChatNotificationJob>(j => j.SendNewMessageEmailAsync(chatMessageId, default));

    public void EnqueueCampaignSend(Guid campaignId) =>
        backgroundJobs.Enqueue<SendCampaignJob>(j => j.RunAsync(campaignId, default));

    public void EnqueueWebhookDelivery(Guid studioId, string eventType, Guid resourceId) =>
        backgroundJobs.Enqueue<WebhookDeliveryJob>(j => j.DeliverAsync(studioId, eventType, resourceId, default));
}
