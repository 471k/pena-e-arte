using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.Appointments.Commands;

public record RescheduleAppointmentCommand(Guid AppointmentId, RescheduleAppointmentRequest Request)
    : IRequest<AppointmentResponse>;

public class RescheduleAppointmentHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    IRealtimeNotifier realtime)
    : IRequestHandler<RescheduleAppointmentCommand, AppointmentResponse>
{
    public async Task<AppointmentResponse> Handle(RescheduleAppointmentCommand command, CancellationToken ct)
    {
        Domain.Entities.Appointment appointment = await db.Appointments
            .Include(a => a.Attachments)
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Appointment), command.AppointmentId);

        bool isClient = currentUser.Role == "client";

        // A client may only reschedule their own appointment — 404 (not 403) on mismatch,
        // matching CancelAppointmentHandler/ReviewDesignHandler's scope-violation convention.
        if (isClient)
        {
            Client? me = await db.FindClientForUserAsync(currentUser, ct);
            if (me is null || me.Id != appointment.ClientId)
                throw new NotFoundException(nameof(Domain.Entities.Appointment), command.AppointmentId);
        }

        if (appointment.Status is AppointmentStatus.Cancelled
                                or AppointmentStatus.Completed
                                or AppointmentStatus.NoShow)
            throw new BusinessRuleViolationException(
                $"Cannot reschedule a {appointment.Status} appointment.");

        // Client self-reschedule is cutoff-gated by the same notice window as self-cancel
        // (Phase 1/2) — reused rather than a second, separate "reschedule window" field.
        // Staff reschedule is unaffected: no notice-window check at all for that path.
        if (isClient)
        {
            DepositRule? rule = await db.DepositRules
                .Where(r => r.IsActive)
                .OrderByDescending(r => r.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            if (!ClientCancellationPolicy.IsWithinNoticeWindow(rule, appointment.Date, DateTime.UtcNow))
            {
                int windowHours = rule?.CancellationWindowHours
                    ?? Domain.Constants.AppointmentSelfServiceDefaults.CancellationWindowHours;
                throw new BusinessRuleViolationException(
                    $"This appointment is less than {windowHours} hours away — please contact the studio directly to reschedule.");
            }
        }

        RescheduleAppointmentRequest req = command.Request;
        DateTime newEnd = req.NewDate.AddMinutes(req.NewDurationMinutes);

        bool conflict = appointment.ArtistId is Guid artistId
            ? await db.Appointments.AnyAsync(a =>
                a.Id != command.AppointmentId &&
                a.ArtistId == artistId &&
                a.Date < newEnd &&
                a.EndDate > req.NewDate &&
                a.Status != AppointmentStatus.Cancelled, ct)
            : !await db.IsAnyArtistAvailableAsync(tenant.StudioId, req.NewDate, req.NewDurationMinutes, ct);

        if (conflict) throw new SlotAlreadyBookedException();

        appointment.Date = req.NewDate;
        appointment.EndDate = newEnd;
        appointment.DurationMinutes = req.NewDurationMinutes;
        appointment.Notes = req.Notes;
        appointment.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        AppointmentResponse response = CreateAppointmentHandler.Map(appointment);
        await realtime.NotifyStudioAsync(tenant.StudioId, "AppointmentUpdated", response, ct);

        return response;
    }
}

public class RescheduleAppointmentValidator : AbstractValidator<RescheduleAppointmentCommand>
{
    // Mirrors CreateAppointmentValidator's ValidDurations / RescheduleDialog.tsx's
    // DURATION_OPTIONS — the same discrete session-length set booking uses, so
    // rescheduling can't be used to set a duration a new booking never could.
    private static readonly int[] ValidDurations = [30, 45, 60, 90, 120, 180, 240, 300, 360, 480];

    public RescheduleAppointmentValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
        RuleFor(x => x.Request.NewDate).GreaterThan(DateTime.UtcNow);
        RuleFor(x => x.Request.NewDurationMinutes)
            .Must(d => ValidDurations.Contains(d))
            .WithMessage($"Duration must be one of: {string.Join(", ", ValidDurations)} minutes.");
    }
}
