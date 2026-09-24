using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Billing.Commands;

/// <summary>Stripe's refund.updated/charge.refund.updated webhook — a status transition on a
/// refund this batch already created (see YearlyRefundIssuer). A status update, not an insert,
/// so re-delivery of the same event is naturally idempotent (no double-processing needed).</summary>
public record HandleRefundUpdatedCommand(string StripeRefundId, string StripeStatus, string? FailureReason) : IRequest;

public class HandleRefundUpdatedHandler(IAppDbContext db, ILogger<HandleRefundUpdatedHandler> logger)
    : IRequestHandler<HandleRefundUpdatedCommand>
{
    public async Task Handle(HandleRefundUpdatedCommand command, CancellationToken ct)
    {
        SubscriptionRefund? refund = await db.SubscriptionRefunds
            .FirstOrDefaultAsync(r => r.StripeRefundId == command.StripeRefundId, ct);

        if (refund is null) return;

        RefundStatus newStatus = command.StripeStatus switch
        {
            "succeeded" => RefundStatus.Succeeded,
            "failed" or "canceled" => RefundStatus.Failed,
            _ => refund.Status, // still pending — leave as-is
        };

        bool transitionedToFailed = newStatus == RefundStatus.Failed && refund.Status != RefundStatus.Failed;

        refund.Status = newStatus;
        refund.UpdatedAt = DateTime.UtcNow;
        if (newStatus == RefundStatus.Failed)
            refund.FailureReason = command.FailureReason ?? refund.FailureReason;

        if (transitionedToFailed)
        {
            // A6: "A Failed refund must alert the admin (error log + visible on the studio's
            // admin page)." The log half — GetAdminStudioSummaryQuery's RecentRefunds covers
            // "visible on the admin page".
            logger.LogError(
                "Stripe refund {@StripeRefundId} transitioned to Failed for subscription {@SubscriptionId}",
                command.StripeRefundId, refund.SubscriptionId);
        }

        await db.SaveChangesAsync(ct);
    }
}
