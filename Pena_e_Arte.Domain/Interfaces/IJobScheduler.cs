namespace Pena_e_Arte.Domain.Interfaces;

public interface IJobScheduler
{
    void ScheduleAppointmentReminder(Guid appointmentId, string type, DateTimeOffset enqueueAt);
    void ScheduleTrialExpiryWarning(Guid studioId, DateTimeOffset enqueueAt);
    void ScheduleTrialExpiry(Guid studioId, DateTimeOffset enqueueAt);
    void ScheduleGracePeriodEnd(Guid studioId, DateTimeOffset enqueueAt);
    void ScheduleDesignRevisionTimeout(Guid revisionId, DateTimeOffset enqueueAt);
    void TriggerIndustryReportNow();
}
