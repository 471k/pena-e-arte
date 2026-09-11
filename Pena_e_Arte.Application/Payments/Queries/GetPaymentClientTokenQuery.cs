using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Queries;

public record GetPaymentClientTokenQuery(Guid PaymentId) : IRequest<PaymentClientTokenResponse>;

public class GetPaymentClientTokenHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetPaymentClientTokenQuery, PaymentClientTokenResponse>
{
    public async Task<PaymentClientTokenResponse> Handle(
        GetPaymentClientTokenQuery query, CancellationToken ct)
    {
        Payment? payment = await db.Payments
            .FirstOrDefaultAsync(p => p.Id == query.PaymentId, ct);

        if (payment is null)
            throw new NotFoundException(nameof(Payment), query.PaymentId);

        if (currentUser.Role == "client")
        {
            Client? client = await db.Clients
                .FirstOrDefaultAsync(c => c.UserId == currentUser.UserId, ct);

            if (client is null || client.Id != payment.ClientId)
                throw new UnauthorizedAccessException("You can only access your own payment details.");
        }

        if (payment.ClientToken is null)
            throw new NotFoundException("ClientToken", query.PaymentId);

        return new PaymentClientTokenResponse(payment.ClientToken);
    }
}
