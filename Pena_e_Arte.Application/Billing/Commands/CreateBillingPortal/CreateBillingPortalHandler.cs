using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Billing.Commands.CreateBillingPortal;

public sealed class CreateBillingPortalHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    IStripeBillingService billing,
    ILogger<CreateBillingPortalHandler> logger)
    : IRequestHandler<CreateBillingPortalCommand, CreateBillingPortalResult>
{
    public async Task<CreateBillingPortalResult> Handle(
        CreateBillingPortalCommand command,
        CancellationToken cancellationToken)
    {
        // Tenant-scoped — global query filters ensure we only see this studio's subscription.
        Domain.Entities.Subscription? sub = await db.Subscriptions
            .Include(s => s.Studio)
            .Where(s => s.StudioId == tenant.StudioId)
            .FirstOrDefaultAsync(cancellationToken);

        if (sub is null || sub.Studio.StripeCustomerId is null)
            throw new NotFoundException(nameof(Domain.Entities.Subscription), tenant.StudioId);

        string portalUrl = await billing.CreatePortalSessionAsync(
            sub.Studio.StripeCustomerId,
            command.ReturnUrl,
            cancellationToken);

        logger.LogInformation(
            "Billing portal session created for studio {@StudioId}", tenant.StudioId);

        return new CreateBillingPortalResult(portalUrl);
    }
}
