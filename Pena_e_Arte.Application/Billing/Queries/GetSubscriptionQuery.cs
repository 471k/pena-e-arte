using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Billing.Queries;

public record GetSubscriptionQuery : IRequest<SubscriptionResponse>;

public class GetSubscriptionHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<GetSubscriptionQuery, SubscriptionResponse>
{
    public async Task<SubscriptionResponse> Handle(GetSubscriptionQuery query, CancellationToken ct)
    {
        Domain.Entities.Subscription subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StudioId == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Subscription), tenant.StudioId);

        return CreateSubscriptionHandler.Map(subscription);
    }
}
