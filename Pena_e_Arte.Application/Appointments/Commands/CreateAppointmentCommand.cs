using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Commands;

public record CreateAppointmentCommand(CreateAppointmentRequest Request) : IRequest<AppointmentResponse>;

public class CreateAppointmentHandler(
    IAppDbContext    db,
    ICurrentTenant   tenant,
    ISlotLocker      slotLocker,
    IJobScheduler    jobs,
    IRealtimeNotifier realtime)
    : IRequestHandler<CreateAppointmentCommand, AppointmentResponse>
{
    public async Task<AppointmentResponse> Handle(CreateAppointmentCommand command, CancellationToken ct)
    {
        CreateAppointmentRequest req = command.Request;
        DateTime requestEnd = req.Date.AddMinutes(req.DurationMinutes);

        bool locked = await slotLocker.TryAcquireLockAsync(tenant.StudioId, req.ArtistId, req.Date, ct);
        if (!locked) throw new SlotAlreadyBookedException();

        try
        {
            bool conflict = await db.Appointments.AnyAsync(a =>
                a.ArtistId == req.ArtistId &&
                a.Date     < requestEnd    &&
                a.EndDate  > req.Date      &&
                a.Status   != AppointmentStatus.Cancelled, ct);

            if (conflict) throw new SlotAlreadyBookedException();

            Appointment appointment = new()
            {
                StudioId        = tenant.StudioId,
                ArtistId        = req.ArtistId,
                ClientId        = req.ClientId,
                Date            = req.Date,
                EndDate         = requestEnd,
                DurationMinutes = req.DurationMinutes,
                Status          = AppointmentStatus.Pending,
                DepositStatus   = DepositStatus.Pending,
                DepositAmount   = req.DepositAmount,
                Notes           = req.Notes
            };

            db.Appointments.Add(appointment);
            await db.SaveChangesAsync(ct);

            jobs.ScheduleAppointmentReminder(appointment.Id, "48h", appointment.Date.AddHours(-48));
            jobs.ScheduleAppointmentReminder(appointment.Id, "24h", appointment.Date.AddHours(-24));

            AppointmentResponse response = Map(appointment);
            await realtime.NotifyStudioAsync(tenant.StudioId, "AppointmentCreated", response, ct);

            return response;
        }
        finally
        {
            await slotLocker.ReleaseLockAsync(tenant.StudioId, req.ArtistId, req.Date, ct);
        }
    }

    internal static AppointmentResponse Map(Appointment a) => new(
        a.Id, a.StudioId, a.ArtistId, a.ClientId,
        a.Date, a.EndDate, a.DurationMinutes,
        a.Status.ToString(), a.DepositStatus.ToString(),
        a.DepositAmount, a.Notes, a.CreatedAt);
}
