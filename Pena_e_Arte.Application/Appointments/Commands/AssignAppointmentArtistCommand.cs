using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.Appointments.Commands;

public record AssignAppointmentArtistCommand(Guid AppointmentId, AssignAppointmentArtistRequest Request)
    : IRequest<AppointmentResponse>, IAuditableCommand
{
    public string AuditAction => AuditActions.AppointmentArtistAssigned;
    public string AuditTargetType => AuditTargetTypes.Appointment;
    public Guid AuditTargetId => AppointmentId;
}

public class AssignAppointmentArtistHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    ISlotLocker slotLocker,
    IRealtimeNotifier realtime,
    ISender sender)
    : IRequestHandler<AssignAppointmentArtistCommand, AppointmentResponse>
{
    public async Task<AppointmentResponse> Handle(AssignAppointmentArtistCommand command, CancellationToken ct)
    {
        Appointment appointment = await db.Appointments
            .Include(a => a.Client)
            .Include(a => a.Attachments)
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Appointment), command.AppointmentId);

        if (appointment.Status is AppointmentStatus.Cancelled
                                or AppointmentStatus.Completed
                                or AppointmentStatus.NoShow)
            throw new BusinessRuleViolationException(
                $"Cannot assign an artist to a {appointment.Status} appointment.");

        Guid artistId = command.Request.ArtistId;

        Artist artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == artistId, ct)
            ?? throw new NotFoundException(nameof(Artist), artistId);
        if (!artist.IsActive)
            throw new BusinessRuleViolationException("Cannot assign an inactive artist.");

        // Mirrors CreateAppointmentCommand's specific-artist validation exactly — a fresh
        // copy, not a shared extraction (Decision #10), to avoid touching that already-
        // working, tested path.
        DayOfWeek day = appointment.Date.DayOfWeek;
        TimeSpan startTime = appointment.Date.TimeOfDay;
        TimeSpan endTime = appointment.EndDate.TimeOfDay;

        var scheduleEntry = await db.ArtistSchedules
            .Where(s => s.ArtistId == artistId && s.DayOfWeek == day && s.IsAvailable)
            .FirstOrDefaultAsync(ct);

        if (scheduleEntry is null)
            throw new BusinessRuleViolationException($"This artist is not available on {day}.");

        if (startTime < scheduleEntry.StartTime || endTime > scheduleEntry.EndTime)
            throw new BusinessRuleViolationException(
                $"Appointment time is outside this artist's working hours ({scheduleEntry.StartTime:hh\\:mm}–{scheduleEntry.EndTime:hh\\:mm}).");

        bool onTimeOff = await db.ArtistTimeOffs.AnyAsync(
            t => t.ArtistId == artistId &&
                 t.StartDate <= appointment.Date.Date &&
                 t.EndDate >= appointment.Date.Date, ct);

        if (onTimeOff)
            throw new BusinessRuleViolationException("This artist is on leave on the appointment's date.");

        bool locked = await slotLocker.TryAcquireLockAsync(tenant.StudioId, artistId, appointment.Date, ct);
        if (!locked) throw new SlotAlreadyBookedException();

        try
        {
            bool conflict = await db.Appointments.AnyAsync(a =>
                a.Id != appointment.Id &&
                a.ArtistId == artistId &&
                a.Date < appointment.EndDate &&
                a.EndDate > appointment.Date &&
                a.Status != AppointmentStatus.Cancelled, ct);

            if (conflict) throw new SlotAlreadyBookedException();

            appointment.ArtistId = artist.Id;

            // Decision #5: recompute a deferred deposit (a percent rule had no artist rate
            // to work from at booking time) now that a real rate is known. A fixed-amount
            // rule was already correct at booking and is untouched by this condition.
            if (appointment.DepositAmount == 0m && appointment.DepositStatus == DepositStatus.Pending)
            {
                DepositRule? rule = await db.DepositRules
                    .Where(r => r.IsActive)
                    .OrderByDescending(r => r.UpdatedAt)
                    .FirstOrDefaultAsync(ct);

                appointment.DepositAmount =
                    DepositCalculator.Calculate(rule, artist.HourlyRate, appointment.DurationMinutes);
            }

            appointment.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            AppointmentResponse response = CreateAppointmentHandler.Map(
                appointment,
                clientName: $"{appointment.Client.FirstName} {appointment.Client.LastName}",
                artistName: $"{artist.FirstName} {artist.LastName}");

            await realtime.NotifyStudioAsync(tenant.StudioId, "AppointmentArtistAssigned", response, ct);
            await sender.Send(new SendAppointmentArtistAssignedNotificationCommand(appointment.Id), ct);

            return response;
        }
        finally
        {
            await slotLocker.ReleaseLockAsync(tenant.StudioId, artistId, appointment.Date, ct);
        }
    }
}

public class AssignAppointmentArtistValidator : AbstractValidator<AssignAppointmentArtistCommand>
{
    public AssignAppointmentArtistValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
        RuleFor(x => x.Request.ArtistId).NotEmpty();
    }
}
