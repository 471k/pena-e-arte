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

namespace Pena_e_Arte.Application.Reminders.Commands;

// AuditTargetId can't be known at construction time — the ManualReminder row this command
// targets doesn't exist until the handler creates it. AuditLogBehavior reads AuditTargetId
// from this same command instance AFTER Handle() completes (confirmed via
// AuditLogBehavior.cs: `TResponse response = await next(ct);` runs first, then the
// IAuditableCommand properties are read), so a mutable property the handler sets just before
// returning works correctly — unlike CancelAppointmentCommand's AuditTargetId, which is a
// pre-existing AppointmentId known up front from the request.
public record CreateManualReminderCommand(CreateManualReminderRequest Request)
    : IRequest<ManualReminderResponse>, IAuditableCommand, IQuotaCheckedCommand
{
    public string AuditAction => AuditActions.ManualReminderSent;
    public string AuditTargetType => AuditTargetTypes.ManualReminder;
    public Guid AuditTargetId { get; set; }

    // Manual reminders write to the same NotificationLog table every other notification-
    // sending command counts against — without this, a studio could blow past its plan's
    // NotificationsPerMonth cap purely via manual SMS, bypassing the enforcement every other
    // notification path already participates in (the flat 20/day/artist Redis quota below is
    // a separate, purpose-built abuse guard — see Decision 6 in architecture.md — not a
    // substitute for plan-tier enforcement).
    public QuotaType QuotaType => QuotaType.NotificationsPerMonth;
}

public class CreateManualReminderHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    IJobScheduler jobs,
    IManualReminderQuotaService quota)
    : IRequestHandler<CreateManualReminderCommand, ManualReminderResponse>
{
    public async Task<ManualReminderResponse> Handle(CreateManualReminderCommand command, CancellationToken ct)
    {
        CreateManualReminderRequest req = command.Request;
        bool isArtist = currentUser.Role == "artist";

        // Only needed for the artist-role ownership checks below and as the artist-role
        // fallback source of truth — owner/issuer callers resolve the target artist per
        // branch instead (from the appointment/client's own ArtistId whenever one exists),
        // so they never need to pass ArtistId just to send a reminder about a specific
        // appointment or an already-assigned client.
        Artist? callerArtist = isArtist
            ? await db.Artists.FirstOrDefaultAsync(a => a.UserId == currentUser.UserId, ct)
                ?? throw new ForbiddenException()
            : null;

        string recipientName;
        string recipientPhone;
        Guid? clientId = null;
        Guid? appointmentId = null;
        Guid resolvedArtistId;

        if (req.AppointmentId is not null)
        {
            Appointment appointment = await db.Appointments
                .Include(a => a.Client)
                .Include(a => a.Artist)
                .FirstOrDefaultAsync(a => a.Id == req.AppointmentId, ct)
                ?? throw new NotFoundException(nameof(Appointment), req.AppointmentId);

            if (isArtist && (appointment.Artist is null || appointment.Artist.UserId != currentUser.UserId))
                throw new NotFoundException(nameof(Appointment), req.AppointmentId);

            if (appointment.Client.Phone is null)
                throw new BusinessRuleViolationException(
                    "This client has no phone number on file — nothing to send a reminder to.");
            if (appointment.Client.SmsOptOut)
                throw new BusinessRuleViolationException("This client has opted out of SMS.");

            recipientName = $"{appointment.Client.FirstName} {appointment.Client.LastName}";
            recipientPhone = appointment.Client.Phone;
            clientId = appointment.ClientId;
            appointmentId = appointment.Id;
            // Authoritative — the appointment's own artist, never req.ArtistId. Prevents an
            // owner/issuer from attributing the reminder (and its quota consumption/audit
            // trail) to an unrelated artist by supplying a mismatched ArtistId.
            resolvedArtistId = appointment.ArtistId
                ?? throw new BusinessRuleViolationException(
                    "Assign an artist to this appointment before sending a reminder.");
        }
        else if (req.ClientId is not null)
        {
            Client client = await db.Clients
                .FirstOrDefaultAsync(c => c.Id == req.ClientId, ct)
                ?? throw new NotFoundException(nameof(Client), req.ClientId);

            if (isArtist && client.ArtistId != callerArtist!.Id)
                throw new NotFoundException(nameof(Client), req.ClientId);

            if (client.Phone is null)
                throw new BusinessRuleViolationException(
                    "This client has no phone number on file — nothing to send a reminder to.");
            if (client.SmsOptOut)
                throw new BusinessRuleViolationException("This client has opted out of SMS.");

            recipientName = $"{client.FirstName} {client.LastName}";
            recipientPhone = client.Phone;
            clientId = client.Id;
            // The client's own assigned artist is authoritative when set; only an Unassigned
            // client (no authoritative source) falls back to requiring an explicit ArtistId.
            resolvedArtistId = isArtist
                ? callerArtist!.Id
                : client.ArtistId ?? await RequireExplicitArtistAsync(req.ArtistId, ct);
        }
        else
        {
            // Raw-contact path — validator has already enforced both fields are present.
            recipientName = req.RecipientName!;
            recipientPhone = req.RecipientPhone!;
            resolvedArtistId = isArtist
                ? callerArtist!.Id
                : await RequireExplicitArtistAsync(req.ArtistId, ct);
        }

        DateTime scheduledFor = req.ScheduledFor ?? DateTime.UtcNow;

        ManualReminder reminder = new()
        {
            StudioId = tenant.StudioId,
            ArtistId = resolvedArtistId,
            AppointmentId = appointmentId,
            ClientId = clientId,
            RecipientName = recipientName,
            RecipientPhone = recipientPhone,
            Message = req.Message,
            ScheduledFor = scheduledFor,
            Status = ManualReminderStatus.Scheduled
        };

        db.ManualReminders.Add(reminder);
        await db.SaveChangesAsync(ct);

        // Quota is checked (and consumed) only after the reminder is durably persisted, not
        // before — otherwise a transient DB failure between the two would waste a day's quota
        // on a reminder that was never actually created. On rejection, the tentatively-saved
        // row is removed so no orphaned reminder is left behind.
        try
        {
            await quota.CheckAndIncrementAsync(tenant.StudioId, resolvedArtistId, ct);
        }
        catch
        {
            db.ManualReminders.Remove(reminder);
            await db.SaveChangesAsync(ct);
            throw;
        }

        reminder.JobId = jobs.ScheduleManualReminder(reminder.Id, scheduledFor);
        await db.SaveChangesAsync(ct);

        command.AuditTargetId = reminder.Id;

        return ToResponse(reminder);
    }

    private async Task<Guid> RequireExplicitArtistAsync(Guid? requestedArtistId, CancellationToken ct)
    {
        // Owner/issuer with no authoritative artist source (raw-contact, or an Unassigned
        // client) must act on behalf of an explicitly named artist (Decision 2) — never
        // silently picking "any" artist at the studio.
        if (requestedArtistId is null)
            throw new BusinessRuleViolationException("ArtistId is required.");

        Artist artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == requestedArtistId, ct)
            ?? throw new NotFoundException(nameof(Artist), requestedArtistId);
        return artist.Id;
    }

    private static ManualReminderResponse ToResponse(ManualReminder r) => new(
        r.Id, r.AppointmentId, r.ClientId, r.RecipientName, r.RecipientPhone, r.Message,
        r.ScheduledFor, r.Status.ToString(), r.SentAt, r.CreatedAt);
}
