using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Webhooks.Commands;

public record DeleteWebhookEndpointCommand : IRequest;

public class DeleteWebhookEndpointHandler(IAppDbContext db)
    : IRequestHandler<DeleteWebhookEndpointCommand>
{
    public async Task Handle(DeleteWebhookEndpointCommand command, CancellationToken ct)
    {
        WebhookEndpoint endpoint = await db.WebhookEndpoints.FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(WebhookEndpoint), Guid.Empty);

        endpoint.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
