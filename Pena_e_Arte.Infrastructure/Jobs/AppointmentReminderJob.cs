using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Infrastructure.Jobs;

public class AppointmentReminderJob(INotificationService notifications, AppDbContext db)
{
    public async Task SendReminderAsync(Guid appointmentId, string type, CancellationToken ct = default)
    {
        var appointment = await db.Appointments.FindAsync([appointmentId], ct);
        if (appointment is null) return;

        // TODO: resolve client contact details and send via notifications.SendEmailAsync / SendSmsAsync
    }
}
