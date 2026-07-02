using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Commands;

public record ConfirmCashDepositCommand(Guid PaymentId) : IRequest<PaymentResponse>;

public class ConfirmCashDepositHandler(IAppDbContext db, ICurrentUser currentUser, ISender sender)
    : IRequestHandler<ConfirmCashDepositCommand, PaymentResponse>
{
    public async Task<PaymentResponse> Handle(ConfirmCashDepositCommand command, CancellationToken ct)
    {
        Payment? payment = await db.Payments
            .Include(p => p.Client)
            .FirstOrDefaultAsync(p => p.Id == command.PaymentId, ct)
            ?? throw new NotFoundException(nameof(Payment), command.PaymentId);

        if (payment.Method != ClientPaymentMethod.Cash)
            throw new BusinessRuleViolationException("This payment is not a cash payment.");

        if (payment.Status != PaymentStatus.CashPending)
            throw new BusinessRuleViolationException("This cash payment has already been confirmed.");

        Appointment? appt = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == payment.AppointmentId, ct);

        if (currentUser.Role == "artist")
        {
            bool ownsAppointment = appt is not null && await db.Artists
                .AnyAsync(a => a.Id == appt.ArtistId && a.UserId == currentUser.UserId, ct);
            if (!ownsAppointment) throw new ForbiddenException();
        }

        payment.Status                = PaymentStatus.Paid;
        payment.PaidAt                = DateTime.UtcNow;
        payment.CashConfirmedByUserId = currentUser.UserId;
        payment.UpdatedAt             = DateTime.UtcNow;

        if (appt is not null)
        {
            appt.DepositStatus = DepositStatus.Paid;
            appt.UpdatedAt     = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        await sender.Send(new SendDepositCapturedNotificationCommand(payment.Id), ct);

        return payment.ToResponse();
    }
}
