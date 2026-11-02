using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.Appointments.Queries;

/// <summary>
/// What cancelling an appointment would do with its deposit, before the caller confirms. Advisory:
/// DELETE /api/v1/appointments/{id} (CancelAppointmentHandler) recomputes it at commit, and the
/// committed result wins. There is no quote token and no expiry — the preview reserves nothing.
/// </summary>
public record GetCancellationPreviewQuery(Guid AppointmentId) : IRequest<CancellationPreviewResponse>;

public class GetCancellationPreviewHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetCancellationPreviewQuery, CancellationPreviewResponse>
{
    public async Task<CancellationPreviewResponse> Handle(GetCancellationPreviewQuery query, CancellationToken ct)
    {
        Appointment appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == query.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Appointment), query.AppointmentId);

        bool isClient = currentUser.Role == "client";

        // Same gate as CancelAppointmentHandler: a client only sees their own appointment, and a
        // mismatch is 404 (not 403) so a guessed id doesn't confirm that it exists.
        if (isClient)
        {
            Client? me = await db.FindClientForUserAsync(currentUser, ct);
            if (me is null || me.Id != appointment.ClientId)
                throw new NotFoundException(nameof(Appointment), query.AppointmentId);
        }

        // The active rule, resolved exactly as CancelAppointmentHandler.ResolveClientRefundPercentAsync does.
        DepositRule? rule = await db.DepositRules
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        DateTime nowUtc = DateTime.UtcNow;
        int refundPercent = isClient
            ? ClientCancellationPolicy.ResolveRefundPercent(rule, appointment.Date, nowUtc)
            : 100;

        Payment? payment = await db.Payments
            .FirstOrDefaultAsync(p => p.AppointmentId == appointment.Id, ct);

        CancellationDecision decision = CancellationOutcome.Decide(payment, refundPercent);

        string currency = payment?.Currency is { Length: > 0 } paymentCurrency
            ? paymentCurrency
            : await db.Studios
                .Where(s => s.Id == appointment.StudioId)
                .Select(s => s.Currency)
                .SingleAsync(ct);

        return new CancellationPreviewResponse(
            appointment.Id,
            ClientCancellationPolicy.NoticeWindowHours(rule),
            ClientCancellationPolicy.IsWithinNoticeWindow(rule, appointment.Date, nowUtc),
            NoticeWindowApplies: isClient,
            refundPercent,
            appointment.DepositAmount,
            currency,
            decision.RefundAmount,
            decision.KeptAmount,
            decision.ClientOutcome.ToString(),
            IsProjection: true);
    }
}

public class GetCancellationPreviewValidator : AbstractValidator<GetCancellationPreviewQuery>
{
    public GetCancellationPreviewValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
    }
}
