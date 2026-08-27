using Hangfire;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;

namespace Pena_e_Arte.Infrastructure.Services;

public class JobScheduler(IBackgroundJobClient backgroundJobs) : IJobScheduler
{
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

    public void EnqueueArtistInvite(string email, string firstName, Guid studioId) =>
        backgroundJobs.Enqueue<SendArtistInviteJob>(j => j.SendAsync(email, firstName, studioId, CancellationToken.None));

    public string ScheduleManualReminder(Guid manualReminderId, DateTimeOffset sendAt) =>
        sendAt <= DateTimeOffset.UtcNow
            ? backgroundJobs.Enqueue<ManualReminderJob>(j => j.SendAsync(manualReminderId, default))
            : backgroundJobs.Schedule<ManualReminderJob>(j => j.SendAsync(manualReminderId, default), sendAt);

    public void CancelJob(string jobId) => backgroundJobs.Delete(jobId);

    public void EnqueueNewMessageEmail(Guid chatMessageId) =>
        backgroundJobs.Enqueue<ChatNotificationJob>(j => j.SendNewMessageEmailAsync(chatMessageId, default));
}
