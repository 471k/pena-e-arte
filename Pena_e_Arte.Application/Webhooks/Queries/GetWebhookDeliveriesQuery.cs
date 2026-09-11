using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Webhooks.Queries;

public record GetWebhookDeliveriesQuery(int Page = 1, int PageSize = 20)
    : IRequest<List<WebhookDeliveryResponse>>;

public class GetWebhookDeliveriesHandler(IAppDbContext db)
    : IRequestHandler<GetWebhookDeliveriesQuery, List<WebhookDeliveryResponse>>
{
    private const int MaxPageSize = 100;

    public async Task<List<WebhookDeliveryResponse>> Handle(GetWebhookDeliveriesQuery query, CancellationToken ct)
    {
        int page = Math.Max(query.Page, 1);
        int pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        return await db.WebhookDeliveries
            .OrderByDescending(d => d.AttemptedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new WebhookDeliveryResponse(
                d.Id, d.EventType, d.ResponseStatusCode, d.Succeeded, d.ErrorMessage, d.AttemptedAt))
            .ToListAsync(ct);
    }
}
