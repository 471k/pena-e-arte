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
    IAppDbContext         db,
    ICurrentTenant        tenant,
    IStripePaymentService stripePayments,
    IRealtimeNotifier     realtime)
    : IRequestHandler<CreatePaymentIntentCommand, PaymentIntentResponse>
{
    public async Task<PaymentIntentResponse> Handle(CreatePaymentIntentCommand command, CancellationToken ct)
    {
        CreatePaymentIntentRequest req = command.Request;

        bool appointmentExists = await db.Appointments
            .AnyAsync(a => a.Id == req.AppointmentId && a.ClientId == req.ClientId, ct);
        if (!appointmentExists)
            throw new NotFoundException(nameof(Appointment), req.AppointmentId);

        bool duplicate = await db.Payments
            .AnyAsync(p => p.AppointmentId == req.AppointmentId && p.Status != PaymentStatus.Failed, ct);
        if (duplicate)
            throw new BusinessRuleViolationException("A payment already exists for this appointment.");

        Studio studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Studio), tenant.StudioId);

        if (studio.StripeAccountId is null)
            throw new StripeAccountNotConnectedException();

        Guid paymentId     = Guid.NewGuid();
        long amountInCents = (long)(req.Amount * 100);

        (string intentId, string clientSecret) = await stripePayments.CreatePaymentIntentAsync(
            studio.StripeAccountId, amountInCents, req.Currency, paymentId, ct);

        Payment payment = new()
        {
            Id                    = paymentId,
            StudioId              = tenant.StudioId,
            AppointmentId         = req.AppointmentId,
            ClientId              = req.ClientId,
            Amount                = req.Amount,
            Status                = PaymentStatus.Pending,
            StripePaymentIntentId = intentId
        };

        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        await realtime.NotifyStudioAsync(tenant.StudioId, "PaymentIntentCreated", Map(payment, []), ct);

        return new PaymentIntentResponse(payment.Id, clientSecret, payment.Status.ToString());
    }

    internal static PaymentResponse Map(Payment p, List<SessionSplitResponse> splits) => new(
        p.Id, p.StudioId, p.AppointmentId, p.ClientId,
        p.Amount, p.Status.ToString(), p.StripePaymentIntentId,
        p.PaidAt, p.CreatedAt, splits);

    internal static SessionSplitResponse MapSplit(SessionSplit ss) => new(
        ss.Id, ss.PaymentId, ss.Label, ss.Amount, ss.PaidAt);
}
