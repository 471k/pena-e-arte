using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Appointments.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.Appointments.Queries;

/// <summary>
/// What marking an appointment as a no-show would do to its deposit, before the artist confirms (E04b,
/// A04). Reads the same payment row (MarkNoShowHandler.FindDepositPaymentAsync) and calls the same
/// decision (NoShowOutcome) as the commit, so they cannot disagree. Advisory; it reserves nothing.
///
/// Authorization matches the commit exactly, so whoever may mark the no-show may see what it will do and
/// nobody else can: an artist only their own assigned session (404 otherwise), owners and admins any
/// session (AssignedArtistAccess; Phi, 2026-11-05).
/// </summary>
public record GetNoShowPreviewQuery(Guid AppointmentId) : IRequest<NoShowPreviewResponse>;

public class GetNoShowPreviewHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetNoShowPreviewQuery, NoShowPreviewResponse>
{
    public async Task<NoShowPreviewResponse> Handle(GetNoShowPreviewQuery query, CancellationToken ct)
    {
        Appointment appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == query.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Appointment), query.AppointmentId);

        if (!await AssignedArtistAccess.MayActOnAsync(db, currentUser, appointment.ArtistId, ct))
            throw new NotFoundException(nameof(Appointment), query.AppointmentId);

        Payment? payment = await MarkNoShowHandler.FindDepositPaymentAsync(db, appointment.Id, ct);
        NoShowDecision decision = NoShowOutcome.Decide(payment);
        bool allowed = NoShowOutcome.IsAllowed(appointment.Status);

        string currency = payment?.Currency is { Length: > 0 } paymentCurrency
            ? paymentCurrency
            : await db.Studios.Where(s => s.Id == appointment.StudioId).Select(s => s.Currency).FirstOrDefaultAsync(ct) ?? string.Empty;

        return new NoShowPreviewResponse(
            appointment.Id,
            allowed,
            allowed ? null : $"Cannot mark no-show for an appointment with status {appointment.Status}.",
            decision.Outcome.ToString(),
            decision.Amount,
            currency);
    }
}

public class GetNoShowPreviewValidator : AbstractValidator<GetNoShowPreviewQuery>
{
    public GetNoShowPreviewValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
    }
}
