using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Queries;

public record GetPaymentByAppointmentQuery(Guid AppointmentId) : IRequest<PaymentResponse?>;

public class GetPaymentByAppointmentHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetPaymentByAppointmentQuery, PaymentResponse?>
{
    public async Task<PaymentResponse?> Handle(GetPaymentByAppointmentQuery query, CancellationToken ct)
    {
        Payment? payment = await db.Payments
            .Include(p => p.Client)
            .Include(p => p.SessionSplits)
            .FirstOrDefaultAsync(p => p.AppointmentId == query.AppointmentId, ct);

        // Clients may only see the payment on their own appointment —
        // ownership resolved (and healed) through Client.UserId / email.
        if (payment is not null && currentUser.Role == "client")
        {
            Client? me = await db.FindClientForUserAsync(currentUser, ct);
            if (me is null || me.Id != payment.ClientId)
                return null;
        }

        return payment?.ToResponse();
    }
}
