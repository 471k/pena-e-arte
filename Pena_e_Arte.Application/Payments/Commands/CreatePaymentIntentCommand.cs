using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Commands;

public record CreatePaymentIntentCommand(CreatePaymentIntentRequest Request) : IRequest<PaymentIntentResponse>;

public class CreatePaymentIntentHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    IPaymentProvider stripePayments,
    IRealtimeNotifier realtime)
    : IRequestHandler<CreatePaymentIntentCommand, PaymentIntentResponse>
{
    public async Task<PaymentIntentResponse> Handle(CreatePaymentIntentCommand command, CancellationToken ct)
    {
        CreatePaymentIntentRequest req = command.Request;

        bool appointmentExists = await db.Appointments
            .AnyAsync(a => a.Id == req.AppointmentId && a.ClientId == req.ClientId, ct);
        if (!appointmentExists)
            throw new NotFoundException(nameof(Appointment), req.AppointmentId);

        // Single payment row per appointment (unique index) — failed attempts are
        // reused in place rather than duplicated.
        Payment? existing = await db.Payments
            .FirstOrDefaultAsync(p => p.AppointmentId == req.AppointmentId, ct);
        if (existing is not null && existing.Status != PaymentStatus.Failed)
            throw new BusinessRuleViolationException("A payment already exists for this appointment.");

        Guid paymentId = existing?.Id ?? Guid.NewGuid();
        long amountInCents = (long)(req.Amount * 100);

        (string intentId, string clientSecret) = await stripePayments.CreatePaymentHoldAsync(
            amountInCents, req.Currency, paymentId, ct);

        Payment payment;
        if (existing is null)
        {
            payment = new Payment
            {
                Id = paymentId,
                StudioId = tenant.StudioId,
                AppointmentId = req.AppointmentId,
                ClientId = req.ClientId,
                Amount = req.Amount,
                Status = PaymentStatus.Pending,
                Method = ClientPaymentMethod.Card,
                ProviderReferenceId = intentId,
                ClientSecret = clientSecret
            };
            db.Payments.Add(payment);
        }
        else
        {
            // Retry after a failed attempt — reuse the row with a fresh intent
            payment = existing;
            payment.Amount = req.Amount;
            payment.Status = PaymentStatus.Pending;
            payment.Method = ClientPaymentMethod.Card;
            payment.ProviderReferenceId = intentId;
            payment.ClientSecret = clientSecret;
            payment.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        await realtime.NotifyStudioAsync(tenant.StudioId, "PaymentIntentCreated", payment.ToResponse(), ct);

        return new PaymentIntentResponse(payment.Id, clientSecret, payment.Status.ToString());
    }
}
