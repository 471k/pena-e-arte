using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Packages.Commands;

/// <summary>
/// Same provider-interaction pattern as CreateDepositPaymentCommand/PurchaseGiftCardCommand:
/// auth hold now, PackagePurchaseReconciliationJob confirms later and grants SessionCount once
/// the provider hold succeeds — this handler never grants sessions itself.
/// </summary>
public record PurchasePackageCommand(PurchasePackageRequest Request) : IRequest<PurchasePackageResponse>;

public class PurchasePackageHandler(IAppDbContext db, ICurrentTenant tenant, ICurrentUser currentUser, IPaymentProvider paymentProvider)
    : IRequestHandler<PurchasePackageCommand, PurchasePackageResponse>
{
    public async Task<PurchasePackageResponse> Handle(PurchasePackageCommand command, CancellationToken ct)
    {
        Client client = await db.FindClientForUserAsync(currentUser, ct)
            ?? throw new NotFoundException(nameof(Client), currentUser.UserId);

        Package package = await db.Packages
            .FirstOrDefaultAsync(p => p.Id == command.Request.PackageId && p.IsActive, ct)
            ?? throw new NotFoundException(nameof(Package), command.Request.PackageId);

        Guid purchaseId = Guid.NewGuid();
        long amountInCents = (long)(package.Price * 100);

        // Same hardcoded "EUR" argument CreateDepositPaymentCommand/PurchaseGiftCardCommand pass —
        // pre-existing inconsistency with Payment.Currency's own "ALL" default; matched for
        // consistency rather than "fixed" as an unrelated side effect of this phase.
        (string providerReferenceId, string clientSecret) = await paymentProvider.CreatePaymentHoldAsync(
            amountInCents, "EUR", purchaseId, ct);

        PackagePurchase purchase = new()
        {
            Id = purchaseId,
            StudioId = tenant.StudioId,
            PackageId = package.Id,
            ClientId = client.Id,
            SessionsRemaining = 0,
            ProviderReferenceId = providerReferenceId,
            ClientSecret = clientSecret,
            Provider = "pok",
        };

        db.PackagePurchases.Add(purchase);
        await db.SaveChangesAsync(ct);

        return new PurchasePackageResponse(purchase.Id, clientSecret);
    }
}
