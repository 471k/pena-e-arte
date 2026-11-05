using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Appointments.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Queries;

/// <summary>
/// Whether the client has been told what changed on an appointment (E04b, A08), from records that
/// already exist: the reschedule's audit entry and staff messages carrying the appointment id
/// (ChatMessage.AppointmentId), and the aftercare step's per-channel log rows (NotificationLog,
/// IsSuccess). It never claims delivery or reading: no record proves either for email or text.
///
/// Scope, like the session preparation read: an artist sees only a session assigned to them (anything
/// else is 404); owners and admins see any session in the studio.
/// </summary>
public record GetAppointmentCommunicationQuery(Guid AppointmentId) : IRequest<AppointmentCommunicationResponse>;

public class GetAppointmentCommunicationHandler(IAppDbContext db, ICurrentUser currentUser, ICurrentTenant tenant)
    : IRequestHandler<GetAppointmentCommunicationQuery, AppointmentCommunicationResponse>
{
    public async Task<AppointmentCommunicationResponse> Handle(GetAppointmentCommunicationQuery query, CancellationToken ct)
    {
        Guid id = query.AppointmentId;
        Appointment appointment = await db.Appointments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(nameof(Appointment), id);

        if (currentUser.Role == "artist")
        {
            Guid? myArtistId = await db.Artists
                .Where(a => a.UserId == currentUser.UserId)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);
            if (myArtistId is null || appointment.ArtistId != myArtistId)
                throw new NotFoundException(nameof(Appointment), id);
        }

        return new AppointmentCommunicationResponse(
            id,
            await RescheduleAsync(appointment, ct),
            appointment.Status == AppointmentStatus.Completed ? await AftercareCommunicationReader.ReadAsync(db, appointment, ct) : null);
    }

    private async Task<RescheduleCommunicationResponse?> RescheduleAsync(Appointment appointment, CancellationToken ct)
    {
        // The audit log has no tenant filter (it isn't a TenantEntity), so the studio is explicit.
        DateTime? rescheduledAt = await db.AuditLogEntries
            .Where(e => e.StudioId == tenant.StudioId
                        && e.TargetId == appointment.Id
                        && e.Action == AuditActions.AppointmentRescheduled)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => (DateTime?)e.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (rescheduledAt is not { } movedAt)
            return null;

        DateTime? messagedAt = await db.ChatMessages
            .Where(m => m.AppointmentId == appointment.Id && m.SenderRole != "client" && m.CreatedAt >= movedAt)
            .OrderBy(m => m.CreatedAt)
            .Select(m => (DateTime?)m.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return new RescheduleCommunicationResponse(messagedAt is null ? "NotSent" : "Sent", movedAt, messagedAt);
    }
}

public class GetAppointmentCommunicationValidator : AbstractValidator<GetAppointmentCommunicationQuery>
{
    public GetAppointmentCommunicationValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
    }
}
