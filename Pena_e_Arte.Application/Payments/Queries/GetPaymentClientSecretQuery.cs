using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Queries;

public record GetPaymentClientSecretQuery(Guid PaymentId) : IRequest<PaymentClientSecretResponse>;

public class GetPaymentClientSecretHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetPaymentClientSecretQuery, PaymentClientSecretResponse>
{
    public async Task<PaymentClientSecretResponse> Handle(
        GetPaymentClientSecretQuery query, CancellationToken ct)
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

        if (payment.ClientSecret is null)
            throw new NotFoundException("ClientSecret", query.PaymentId);

        return new PaymentClientSecretResponse(payment.ClientSecret);
    }
}
