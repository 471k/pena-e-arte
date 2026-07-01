namespace Pena_e_Arte.Domain.Interfaces;

public interface IJobScheduler
{
    string ScheduleAppointmentReminder(Guid appointmentId, string type, DateTimeOffset enqueueAt);
    void   CancelAppointmentJobs(string? jobId48h, string? jobId24h);

    void ScheduleTrialExpiryWarning(Guid studioId, DateTimeOffset enqueueAt);
    void ScheduleTrialExpiry(Guid studioId, DateTimeOffset enqueueAt);
    void ScheduleGracePeriodEnd(Guid studioId, DateTimeOffset enqueueAt);
    void ScheduleDesignRevisionTimeout(Guid revisionId, DateTimeOffset enqueueAt);
    void TriggerIndustryReportNow();
    void EnqueueArtistInvite(string email, string firstName, Guid studioId);
}
