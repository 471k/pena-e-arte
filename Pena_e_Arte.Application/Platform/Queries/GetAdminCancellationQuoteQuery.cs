using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Billing;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Platform.Queries;

/// <summary>What cancelling this studio's subscription will do, before the admin confirms (slice 8 phase 3, D03).
/// Built by <see cref="AdminCancellationQuotes"/>, the same rule the cancel command re-applies and compares.</summary>
public record GetAdminCancellationQuoteQuery(Guid StudioId) : IRequest<AdminCancellationQuoteResponse>;

public class GetAdminCancellationQuoteHandler(IAppDbContext db, TimeProvider clock)
    : IRequestHandler<GetAdminCancellationQuoteQuery, AdminCancellationQuoteResponse>
{
    public async Task<AdminCancellationQuoteResponse> Handle(GetAdminCancellationQuoteQuery query, CancellationToken ct)
    {
        // Subscription and Plan carry no tenant query filter (Subscription is deliberately not a TenantEntity, like
        // SubscriptionRefund), so this cross-tenant admin read needs no IgnoreQueryFilters. AdminOnly endpoint.
        Subscription subscription = await db.Subscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.StudioId == query.StudioId, ct)
            ?? throw new BusinessRuleViolationException("Studio has no subscription to cancel.");

        // Same refusals as the command, so the review never offers a cancel the server would refuse.
        if (!AdminCancellationQuotes.Cancellable.Contains(subscription.Status))
            throw new BusinessRuleViolationException(
                $"A subscription with status '{subscription.Status}' cannot be cancelled.");

        SubscriptionInvoicePayment? invoice = await CancellationQuotes.LatestInvoiceAsync(db, subscription, ct);
        return AdminCancellationQuotes.Build(subscription, invoice, clock.GetUtcNow().UtcDateTime);
    }
}

public class GetAdminCancellationQuoteValidator : AbstractValidator<GetAdminCancellationQuoteQuery>
{
    public GetAdminCancellationQuoteValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
    }
}
