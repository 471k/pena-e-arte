using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Webhooks.Commands;

public record SendTestWebhookEventCommand : IRequest;

public class SendTestWebhookEventHandler(IAppDbContext db, ICurrentTenant tenant, IJobScheduler jobs)
    : IRequestHandler<SendTestWebhookEventCommand>
{
    public async Task Handle(SendTestWebhookEventCommand command, CancellationToken ct)
    {
        bool hasActiveEndpoint = await db.WebhookEndpoints.AnyAsync(w => w.IsActive, ct);
        if (!hasActiveEndpoint)
            throw new BusinessRuleViolationException("Configure an active webhook endpoint first.");

        jobs.EnqueueWebhookDelivery(tenant.StudioId, "ping", Guid.Empty);
    }
}
