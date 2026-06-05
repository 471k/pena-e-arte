using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Commands;

public record RescheduleAppointmentCommand(Guid AppointmentId, RescheduleAppointmentRequest Request)
    : IRequest<AppointmentResponse>;

public class RescheduleAppointmentHandler(
    IAppDbContext     db,
    ICurrentTenant    tenant,
    IRealtimeNotifier realtime)
    : IRequestHandler<RescheduleAppointmentCommand, AppointmentResponse>
{
    public async Task<AppointmentResponse> Handle(RescheduleAppointmentCommand command, CancellationToken ct)
    {
        Domain.Entities.Appointment appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Appointment), command.AppointmentId);

        if (appointment.Status is AppointmentStatus.Cancelled
                                or AppointmentStatus.Completed
                                or AppointmentStatus.NoShow)
            throw new BusinessRuleViolationException(
                $"Cannot reschedule a {appointment.Status} appointment.");

        RescheduleAppointmentRequest req = command.Request;
        DateTime newEnd = req.NewDate.AddMinutes(req.NewDurationMinutes);

        bool conflict = await db.Appointments.AnyAsync(a =>
            a.Id       != command.AppointmentId &&
            a.ArtistId == appointment.ArtistId  &&
            a.Date     < newEnd                 &&
            a.EndDate  > req.NewDate            &&
            a.Status   != AppointmentStatus.Cancelled, ct);

        if (conflict) throw new SlotAlreadyBookedException();

        appointment.Date            = req.NewDate;
        appointment.EndDate         = newEnd;
        appointment.DurationMinutes = req.NewDurationMinutes;
        appointment.Notes           = req.Notes;
        appointment.UpdatedAt       = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        AppointmentResponse response = CreateAppointmentHandler.Map(appointment);
        await realtime.NotifyStudioAsync(tenant.StudioId, "AppointmentUpdated", response, ct);

        return response;
    }
}

public class RescheduleAppointmentValidator : AbstractValidator<RescheduleAppointmentCommand>
{
    public RescheduleAppointmentValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
        RuleFor(x => x.Request.NewDate).GreaterThan(DateTime.UtcNow);
        RuleFor(x => x.Request.NewDurationMinutes).InclusiveBetween(30, 480);
    }
}
