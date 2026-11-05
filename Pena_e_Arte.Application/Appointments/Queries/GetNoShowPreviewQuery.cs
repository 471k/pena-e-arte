using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.Appointments.Queries;

/// <summary>
/// What marking an appointment as a no-show would do to its deposit, before the artist confirms (E04b,
/// A04). Reads the same payment row (MarkNoShowHandler.FindDepositPaymentAsync) and calls the same
/// decision (NoShowOutcome) as the commit, so they cannot disagree. Advisory; it reserves nothing.
///
/// Authorization deliberately matches the commit (ArtistAndAbove, no artist-ownership check): whoever may
/// mark the no-show may see what it will do. The commit's own missing ownership check is flagged for Phi
/// (E04b prompt §2.2) and not changed here; scoping only the preview would quietly block colleagues'
/// no-shows in the UI, which is the very decision that is Phi's to make.
/// </summary>
public record GetNoShowPreviewQuery(Guid AppointmentId) : IRequest<NoShowPreviewResponse>;

public class GetNoShowPreviewHandler(IAppDbContext db)
    : IRequestHandler<GetNoShowPreviewQuery, NoShowPreviewResponse>
{
    public async Task<NoShowPreviewResponse> Handle(GetNoShowPreviewQuery query, CancellationToken ct)
    {
        Appointment appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == query.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Appointment), query.AppointmentId);

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
