using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;
using Pena_e_Arte.Application.Waitlists.Commands;
using Pena_e_Arte.Application.Waitlists.Common;

namespace Pena_e_Arte.Application.Appointments.Commands;

// AuditStudioId is left at its default (null) — AuditLogBehavior falls back to the caller's
// ICurrentTenant.StudioId, same as CancelAppointmentCommand right above it in the audit log.
public record RescheduleAppointmentCommand(Guid AppointmentId, RescheduleAppointmentRequest Request)
    : IRequest<AppointmentResponse>, IAuditableCommand
{
    public string AuditAction => AuditActions.AppointmentRescheduled;
    public string AuditTargetType => AuditTargetTypes.Appointment;
    public Guid AuditTargetId => AppointmentId;
}

public class RescheduleAppointmentHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    IRealtimeNotifier realtime,
    ISender sender,
    IJobScheduler jobs)
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

        // Status gate, then (client only) the notice window — decided by RescheduleEligibility, the same
        // function the reschedule preview calls, so the dialog and the save can't disagree (E04b, A02).
        // Staff reschedule is unaffected by the notice window. The rule is only read for a client, as before.
        DepositRule? rule = isClient
            ? await db.DepositRules
                .Where(r => r.IsActive)
                .OrderByDescending(r => r.UpdatedAt)
                .FirstOrDefaultAsync(ct)
            : null;

        RescheduleEligibilityDecision eligibility = RescheduleEligibility.Decide(
            appointment.Status, isClient, rule, appointment.Date, DateTime.UtcNow);
        if (!eligibility.Allowed)
            throw new BusinessRuleViolationException(RescheduleEligibility.Message(eligibility, appointment.Status)!);

        RescheduleAppointmentRequest req = command.Request;
        DateTime newEnd = req.NewDate.AddMinutes(req.NewDurationMinutes);

        // The artist must actually be working then: studio closures and hours, the artist's own hours
        // and time off, the same schedule check booking makes before its lock and the reschedule preview
        // shows (Phi, 2026-11-05). Until now the save checked only overlaps for an assigned artist, so an
        // API caller (a client included) could move a session outside opening hours. Applies to staff as
        // well; the dialog already refused such a time for everyone. An unassigned appointment is checked
        // by IsAnyArtistAvailableAsync below, which already includes hours.
        if (appointment.ArtistId is Guid scheduleArtistId)
        {
            (bool available, string? reason) = await db.CheckArtistScheduleAsync(
                appointment.StudioId, scheduleArtistId, req.NewDate, req.NewDurationMinutes, ct);
            if (!available)
                throw new BusinessRuleViolationException(reason ?? "The artist is not available at that time.");
        }

        // Moving an appointment into an artist's time is a booking too: the same artist booking lock
        // as CreateAppointmentCoreAsync, held from the overlap check until the move is saved, so two
        // concurrent requests can't both land in overlapping time. No artist yet: nothing to claim.
        await using IBookingClaim? claim = appointment.ArtistId is Guid claimArtistId
            ? await db.ClaimArtistForBookingAsync(appointment.StudioId, claimArtistId, ct)
            : null;

        bool conflict = appointment.ArtistId is Guid artistId
            ? await db.Appointments.AnyAsync(a =>
                a.Id != command.AppointmentId &&
                a.ArtistId == artistId &&
                a.Date < newEnd &&
                a.EndDate > req.NewDate &&
                a.Status != AppointmentStatus.Cancelled, ct)
            : !await db.IsAnyArtistAvailableAsync(tenant.StudioId, req.NewDate, req.NewDurationMinutes, ct);

        if (conflict) throw new SlotAlreadyBookedException();

        // The original slot (pre-mutation Date/ArtistId/duration) is what frees up — capture it before
        // overwriting, then run the same auto-FIFO waitlist match CancelAppointmentHandler uses.
        DateTime freedDate = appointment.Date;
        Guid? freedArtistId = appointment.ArtistId;
        int freedDurationMinutes = appointment.DurationMinutes;

        appointment.Date = req.NewDate;
        appointment.EndDate = newEnd;
        appointment.DurationMinutes = req.NewDurationMinutes;
        appointment.Notes = req.Notes;
        appointment.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        if (claim is not null)
            await claim.CommitAsync(ct);

        // After the move is saved, not before: the match now checks the freed slot is really free (C09), and
        // until the save the database still shows this appointment there. Excluding it instead would also hide
        // its new position, which can overlap the old slot when the move is short.
        Guid? waitlistMatchId = await db.ClaimNextMatchAsync(
            appointment.StudioId, freedArtistId, freedDate, freedDurationMinutes, freedAppointmentId: null, ct);
        if (waitlistMatchId is not null)
            await db.SaveChangesAsync(ct);

        AppointmentResponse response = CreateAppointmentHandler.Map(appointment);
        await realtime.NotifyStudioAsync(tenant.StudioId, "AppointmentUpdated", response, ct);
        jobs.EnqueueWebhookDelivery(tenant.StudioId, "appointment.rescheduled", appointment.Id);

        if (waitlistMatchId is Guid matchId)
            await sender.Send(new SendWaitlistSlotAvailableNotificationCommand(matchId), ct);

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
