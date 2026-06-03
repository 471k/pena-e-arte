using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Payments.Queries;

public record GetPaymentByAppointmentQuery(Guid AppointmentId) : IRequest<PaymentResponse?>;

public class GetPaymentByAppointmentHandler(IAppDbContext db)
    : IRequestHandler<GetPaymentByAppointmentQuery, PaymentResponse?>
{
    public async Task<PaymentResponse?> Handle(GetPaymentByAppointmentQuery query, CancellationToken ct)
    {
        Payment? payment = await db.Payments
            .Include(p => p.SessionSplits.Where(ss => ss.DeletedAt == null))
            .FirstOrDefaultAsync(p => p.AppointmentId == query.AppointmentId, ct);

        if (payment is null) return null;

        List<SessionSplitResponse> splits = payment.SessionSplits
            .Select(CreatePaymentIntentHandler.MapSplit).ToList();

        return CreatePaymentIntentHandler.Map(payment, splits);
    }
}
