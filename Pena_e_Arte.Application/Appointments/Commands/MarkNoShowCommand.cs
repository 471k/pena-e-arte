using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Appointments.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.Appointments.Commands;

public record MarkNoShowCommand(Guid AppointmentId) : IRequest<NoShowResultResponse>;

public class MarkNoShowHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    IRealtimeNotifier realtime,
    IPaymentProvider paymentProvider,
    ICurrentUser currentUser)
    : IRequestHandler<MarkNoShowCommand, NoShowResultResponse>
{
    /// <summary>
    /// The deposit payment a no-show acts on. Shared with the no-show preview so both read the same row.
    /// </summary>
    internal static Task<Domain.Entities.Payment?> FindDepositPaymentAsync(
        IAppDbContext db, Guid appointmentId, CancellationToken ct) =>
        db.Payments.FirstOrDefaultAsync(p =>
            p.AppointmentId == appointmentId &&
            p.Status != PaymentStatus.Refunded, ct);

    public async Task<NoShowResultResponse> Handle(MarkNoShowCommand command, CancellationToken ct)
    {
        Domain.Entities.Appointment appointment = await db.Appointments
            .Include(a => a.Attachments)
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Appointment), command.AppointmentId);

        // A no-show moves money on the session, so an artist may only mark their own (Phi, 2026-11-05;
        // E04b prompt §2.2). Owners and admins can mark any session in the studio. 404, never 403.
        if (!await AssignedArtistAccess.MayActOnAsync(db, currentUser, appointment.ArtistId, ct))
            throw new NotFoundException(nameof(Domain.Entities.Appointment), command.AppointmentId);

        if (!NoShowOutcome.IsAllowed(appointment.Status))
            throw new BusinessRuleViolationException(
                $"Cannot mark no-show for an appointment with status {appointment.Status}.");

        appointment.Status = AppointmentStatus.NoShow;
        appointment.DepositStatus = DepositStatus.Forfeited;
        appointment.UpdatedAt = DateTime.UtcNow;

        Domain.Entities.Payment? payment = await FindDepositPaymentAsync(db, appointment.Id, ct);

        // What to do with the deposit is decided by NoShowOutcome, the same function the no-show preview
        // calls (E04b, A04). The writes below are exactly the ones this handler made before the decision
        // was extracted, in the same order, including the cash branch's missing PaidAt (prompt §2.1:
        // flagged for Phi, not changed).
        NoShowDecision decision = NoShowOutcome.Decide(payment);
        switch (decision.PaymentAction)
        {
            case NoShowPaymentAction.CaptureHold:
                await paymentProvider.CaptureAsync(payment!.StudioId, payment.ProviderReferenceId!, ct);
                payment.Status = PaymentStatus.Paid;
                payment.PaidAt = DateTime.UtcNow;       // revenue reports filter on PaidAt != null
                payment.UpdatedAt = DateTime.UtcNow;
                break;
            case NoShowPaymentAction.RecordCashAsPaid:
                // Cash deposit declared but not yet confirmed — forfeit the record
                payment!.Status = PaymentStatus.Paid;
                payment.UpdatedAt = DateTime.UtcNow;
                break;
        }

        await db.SaveChangesAsync(ct);

        AppointmentResponse response = CreateAppointmentHandler.Map(appointment);
        await realtime.NotifyStudioAsync(
            tenant.StudioId, "AppointmentNoShow", response, ct);

        string currency = payment?.Currency is { Length: > 0 } paymentCurrency
            ? paymentCurrency
            : await db.Studios.Where(s => s.Id == appointment.StudioId).Select(s => s.Currency).FirstOrDefaultAsync(ct) ?? string.Empty;

        // The server's actual result: the decision this commit applied, not the earlier projection.
        return new NoShowResultResponse(
            response, decision.Outcome.ToString(), decision.Amount, currency, payment?.Status.ToString());
    }
}
