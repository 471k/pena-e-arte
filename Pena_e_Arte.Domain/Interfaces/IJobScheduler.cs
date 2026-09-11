namespace Pena_e_Arte.Domain.Interfaces;

public interface IJobScheduler
{
    string ScheduleAppointmentReminder(Guid appointmentId, string type, DateTimeOffset enqueueAt);
    void CancelAppointmentJobs(string? jobId48h, string? jobId24h);

    void ScheduleTrialExpiryWarning(Guid studioId, DateTimeOffset enqueueAt);
    void ScheduleTrialExpiry(Guid studioId, DateTimeOffset enqueueAt);
    void ScheduleGracePeriodEnd(Guid studioId, DateTimeOffset enqueueAt);
    void ScheduleDesignRevisionTimeout(Guid revisionId, DateTimeOffset enqueueAt);
    void TriggerIndustryReportNow();
    void EnqueueArtistInvite(string email, string firstName, Guid studioId);

    string ScheduleManualReminder(Guid manualReminderId, DateTimeOffset sendAt);
    void CancelJob(string jobId);
    void EnqueueNewMessageEmail(Guid chatMessageId);
    void EnqueueCampaignSend(Guid campaignId);

    void EnqueueWebhookDelivery(Guid studioId, string eventType, Guid resourceId);

    /// <summary>
    /// Runs PaymentReconciliationJob immediately rather than waiting for its normal schedule.
    /// The POK inbound webhook calls this — POK's webhook payload is undocumented and unsigned
    /// (ADR-0001), so the handler never tries to parse it into "which payment changed"; it only
    /// treats the POST as a hint to re-check sooner, and the job itself re-fetches every
    /// in-flight payment's real state from POK rather than trusting anything from the request.
    /// </summary>
    void TriggerPaymentReconciliationNow();
}
