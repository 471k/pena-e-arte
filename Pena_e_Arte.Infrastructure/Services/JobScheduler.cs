using Hangfire;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;

namespace Pena_e_Arte.Infrastructure.Services;

public class JobScheduler(IBackgroundJobClient backgroundJobs) : IJobScheduler
{
    public void ScheduleAppointmentReminder(Guid appointmentId, string type, DateTimeOffset enqueueAt) =>
        backgroundJobs.Schedule<AppointmentReminderJob>(
            j => j.SendReminderAsync(appointmentId, type, default), enqueueAt);

    public void ScheduleTrialExpiryWarning(Guid studioId, DateTimeOffset enqueueAt) =>
        backgroundJobs.Schedule<TrialExpiryWarningJob>(
            j => j.ExecuteAsync(studioId, default), enqueueAt);

    public void ScheduleTrialExpiry(Guid studioId, DateTimeOffset enqueueAt) =>
        backgroundJobs.Schedule<TrialExpiryJob>(
            j => j.ExecuteAsync(studioId, default), enqueueAt);

    public void ScheduleGracePeriodEnd(Guid studioId, DateTimeOffset enqueueAt) =>
        backgroundJobs.Schedule<GracePeriodEndJob>(
            j => j.ExecuteAsync(studioId, default), enqueueAt);
}
