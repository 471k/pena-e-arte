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
    : IRequest<ManualReminderResponse>, IAuditableCommand
{
    public string AuditAction => AuditActions.ManualReminderSent;
    public string AuditTargetType => AuditTargetTypes.ManualReminder;
    public Guid AuditTargetId { get; set; }
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

        Artist artist = await ResolveArtistAsync(req.ArtistId, ct);

        string recipientName;
        string recipientPhone;
        Guid? clientId = null;
        Guid? appointmentId = null;

        if (req.AppointmentId is not null)
        {
            Appointment appointment = await db.Appointments
                .Include(a => a.Client)
                .Include(a => a.Artist)
                .FirstOrDefaultAsync(a => a.Id == req.AppointmentId, ct)
                ?? throw new NotFoundException(nameof(Appointment), req.AppointmentId);

            if (currentUser.Role == "artist" && appointment.Artist.UserId != currentUser.UserId)
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
        }
        else if (req.ClientId is not null)
        {
            Client client = await db.Clients
                .FirstOrDefaultAsync(c => c.Id == req.ClientId, ct)
                ?? throw new NotFoundException(nameof(Client), req.ClientId);

            if (currentUser.Role == "artist" && client.ArtistId != artist.Id)
                throw new NotFoundException(nameof(Client), req.ClientId);

            if (client.Phone is null)
                throw new BusinessRuleViolationException(
                    "This client has no phone number on file — nothing to send a reminder to.");
            if (client.SmsOptOut)
                throw new BusinessRuleViolationException("This client has opted out of SMS.");

            recipientName = $"{client.FirstName} {client.LastName}";
            recipientPhone = client.Phone;
            clientId = client.Id;
        }
        else
        {
            // Raw-contact path — validator has already enforced both fields are present.
            recipientName = req.RecipientName!;
            recipientPhone = req.RecipientPhone!;
        }

        await quota.CheckAndIncrementAsync(tenant.StudioId, artist.Id, ct);

        DateTime scheduledFor = req.ScheduledFor ?? DateTime.UtcNow;

        ManualReminder reminder = new()
        {
            StudioId = tenant.StudioId,
            ArtistId = artist.Id,
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

        reminder.JobId = jobs.ScheduleManualReminder(reminder.Id, scheduledFor);
        await db.SaveChangesAsync(ct);

        command.AuditTargetId = reminder.Id;

        return ToResponse(reminder);
    }

    private async Task<Artist> ResolveArtistAsync(Guid? requestedArtistId, CancellationToken ct)
    {
        if (currentUser.Role == "artist")
        {
            return await db.Artists.FirstOrDefaultAsync(a => a.UserId == currentUser.UserId, ct)
                ?? throw new ForbiddenException();
        }

        // Owner/issuer: act on behalf of the requested artist (Decision 2). Require it explicitly
        // rather than silently picking "any" artist at the studio.
        if (requestedArtistId is null)
            throw new BusinessRuleViolationException("ArtistId is required.");

        return await db.Artists.FirstOrDefaultAsync(a => a.Id == requestedArtistId, ct)
            ?? throw new NotFoundException(nameof(Artist), requestedArtistId);
    }

    private static ManualReminderResponse ToResponse(ManualReminder r) => new(
        r.Id, r.AppointmentId, r.ClientId, r.RecipientName, r.RecipientPhone, r.Message,
        r.ScheduledFor, r.Status.ToString(), r.SentAt, r.CreatedAt);
}
