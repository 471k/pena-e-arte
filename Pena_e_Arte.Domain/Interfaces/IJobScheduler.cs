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

    /// <summary>
    /// isRejoiningArtist: true when this reuses an existing Identity account that already has
    /// working login credentials (a previously-removed artist being added to a new studio) —
    /// the email copy differs from a brand-new account's "set your password" invite since the
    /// recipient does not need to set anything to already be able to sign in.
    /// </summary>
    void EnqueueArtistInvite(string email, string firstName, Guid studioId, bool isRejoiningArtist = false);

    string ScheduleManualReminder(Guid manualReminderId, DateTimeOffset sendAt);
    void CancelJob(string jobId);
    void EnqueueNewMessageEmail(Guid chatMessageId);
    void EnqueueCampaignSend(Guid campaignId);

    void EnqueueWebhookDelivery(Guid studioId, string eventType, Guid resourceId);

    /// <summary>
    /// Runs PaymentReconciliationJob within ~10 s rather than waiting for its normal schedule; a burst
    /// of triggers inside that window becomes one run (C-5 — POK sends each webhook 3 times in ~3 s).
    /// The POK inbound webhook calls this — POK's webhook payload is undocumented and unsigned
    /// (ADR-0001), so the handler never tries to parse it into "which payment changed"; it only
    /// treats the POST as a hint to re-check sooner, and the job itself re-fetches every
    /// in-flight payment's real state from POK rather than trusting anything from the request.
    /// </summary>
    void TriggerPaymentReconciliationNow();

    /// <summary>Emails the studio owner (and adds a bell entry) that a held card deposit can no longer be
    /// charged because POK ended the hold (C-15). Its own job, so a mail failure never touches reconciliation.</summary>
    void EnqueueDepositHoldLostNotification(Guid paymentId);
}
