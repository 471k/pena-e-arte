using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Messaging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.Appointments.Queries;

/// <summary>
/// One of the caller's own appointments, for the client appointment page (DEC02). It exists so a
/// bookmark or an email link to one appointment resolves after sign-in, and to carry the itemised
/// blocks (readiness) that would bloat every row of GET /appointments/mine. It is not an authorization
/// fix: /mine already returns the client their own appointments, and the staff read
/// (GetAppointmentQuery, ArtistAndAbove) is unchanged.
/// </summary>
public record GetMyAppointmentQuery(Guid AppointmentId) : IRequest<ClientAppointmentDetailResponse>;

public class GetMyAppointmentHandler(IAppDbContext db, ICurrentUser currentUser, IIdentityService identity)
    : IRequestHandler<GetMyAppointmentQuery, ClientAppointmentDetailResponse>
{
    public async Task<ClientAppointmentDetailResponse> Handle(GetMyAppointmentQuery query, CancellationToken ct)
    {
        Guid id = query.AppointmentId;

        Appointment appointment = await db.Appointments
            .Include(a => a.Artist)
            .Include(a => a.Attachments)
            .Include(a => a.Service)
            .Include(a => a.Intake)
            .FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Appointment), id);

        // The same gate as the client's other reads: someone else's appointment is 404, never 403, so a
        // guessed id doesn't confirm that it exists.
        Client? me = await db.FindClientForUserAsync(currentUser, ct);
        if (me is null || me.Id != appointment.ClientId)
            throw new NotFoundException(nameof(Domain.Entities.Appointment), id);

        // The active rule, resolved exactly as GetMyAppointmentsHandler resolves it.
        DepositRule? rule = await db.DepositRules
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        Dictionary<Guid, ReadinessFacts> readiness = await AppointmentReadinessReader.LoadAsync(
            db, db.Appointments.Where(a => a.Id == id), ct);
        ReadinessFacts facts = readiness[id];
        IReadOnlyList<ReadinessItem> items = AppointmentReadiness.Derive(facts);

        AppointmentResponse mapped = CreateAppointmentHandler.Map(
            appointment,
            artistName: appointment.Artist is not null
                ? $"{appointment.Artist.FirstName} {appointment.Artist.LastName}"
                : null) with
        {
            // Medical information the client page does not display; /mine does not carry it either.
            SafetyNotes = null,
            SelfService = new AppointmentSelfServiceResponse(
                ClientCancellationPolicy.NoticeWindowHours(rule),
                ClientCancellationPolicy.IsWithinNoticeWindow(rule, appointment.Date, DateTime.UtcNow)),
            Readiness = AppointmentReadinessReader.Summarize(items),
        };

        return new ClientAppointmentDetailResponse(
            mapped,
            AppointmentReadinessReader.Itemise(items, facts, id),
            await ResolveContactAsync(appointment, ct));
    }

    // DEC06: the same people ConversationEligibility lets a client message: an active artist with a
    // login on one of their appointments, or the studio owner by the one shared owner rule.
    private async Task<ClientAppointmentContactResponse> ResolveContactAsync(Appointment appointment, CancellationToken ct)
    {
        StudioContact studio = await db.Studios
            .Where(s => s.Id == appointment.StudioId)
            .Select(s => new StudioContact(s.Name, s.PhoneNumber, s.InstagramHandle))
            .SingleAsync(ct);

        Guid? recipientId = null;
        string? recipientRole = null;
        string? recipientName = null;

        if (appointment.Artist is { UserId: { } artistUserId, IsActive: true } artist)
        {
            recipientId = artistUserId;
            recipientRole = "artist";
            recipientName = $"{artist.FirstName} {artist.LastName}";
        }
        else
        {
            (Guid? ownerId, string ownerName) = await StudioOwnerResolver.ResolveAsync(
                db, identity, appointment.StudioId, ct);
            if (ownerId is { } id && id != currentUser.UserId)
            {
                recipientId = id;
                recipientRole = "owner";
                recipientName = ownerName;
            }
        }

        return new ClientAppointmentContactResponse(
            recipientId, recipientRole, recipientName,
            studio.Name,
            string.IsNullOrWhiteSpace(studio.Phone) ? null : studio.Phone,
            string.IsNullOrWhiteSpace(studio.Instagram) ? null : studio.Instagram);
    }

    private sealed record StudioContact(string Name, string? Phone, string? Instagram);
}

public class GetMyAppointmentValidator : AbstractValidator<GetMyAppointmentQuery>
{
    public GetMyAppointmentValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
    }
}
