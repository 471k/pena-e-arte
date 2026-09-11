using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Webhooks.Queries;

public record GetWebhookEndpointStatusQuery : IRequest<WebhookEndpointStatusResponse>;

public class GetWebhookEndpointStatusHandler(IAppDbContext db)
    : IRequestHandler<GetWebhookEndpointStatusQuery, WebhookEndpointStatusResponse>
{
    public async Task<WebhookEndpointStatusResponse> Handle(GetWebhookEndpointStatusQuery query, CancellationToken ct)
    {
        WebhookEndpoint? endpoint = await db.WebhookEndpoints.FirstOrDefaultAsync(ct);

        return endpoint is null
            ? new WebhookEndpointStatusResponse(false, null, false, null, null, null)
            : new WebhookEndpointStatusResponse(
                true, endpoint.Url, endpoint.IsActive, endpoint.CreatedAt,
                endpoint.LastDeliveryAt, endpoint.LastDeliverySucceeded);
    }
}
