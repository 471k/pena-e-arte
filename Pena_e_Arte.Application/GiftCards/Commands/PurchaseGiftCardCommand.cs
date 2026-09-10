using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.GiftCards.Commands;

/// <summary>
/// Structural clone of CreateDepositPaymentCommand's provider-interaction pattern: auth hold now,
/// webhook/reconciliation confirms later, capture happens there. Correction to the original P1
/// backlog spec — the deleted IStripePaymentService never existed as a callable interface here;
/// IPaymentProvider (currently NullPaymentProvider, failing closed until POK lands) is the only
/// correct pattern, same as every other client-facing card payment in this codebase. See
/// architecture.md Decisions Log.
/// </summary>
public record PurchaseGiftCardCommand(PurchaseGiftCardRequest Request) : IRequest<PurchaseGiftCardResponse>;

public class PurchaseGiftCardHandler(IAppDbContext db, IPaymentProvider paymentProvider)
    : IRequestHandler<PurchaseGiftCardCommand, PurchaseGiftCardResponse>
{
    public async Task<PurchaseGiftCardResponse> Handle(PurchaseGiftCardCommand command, CancellationToken ct)
    {
        PurchaseGiftCardRequest req = command.Request;

        Studio studio = await db.GetPublishedStudioBySlugAsync(req.StudioSlug, ct)
            ?? throw new NotFoundException(nameof(Studio), req.StudioSlug);

        string code = await GenerateUniqueCodeAsync(studio.Id, ct);
        Guid giftCardId = Guid.NewGuid();
        long amountInCents = (long)(req.Amount * 100);

        // Same hardcoded "EUR" argument CreateDepositPaymentCommand passes — pre-existing
        // inconsistency with Payment.Currency's own "ALL" default; matched for consistency
        // rather than "fixed" as an unrelated side effect of this phase.
        (string providerReferenceId, string clientSecret) = await paymentProvider.CreatePaymentHoldAsync(
            amountInCents, "EUR", giftCardId, ct);

        GiftCard giftCard = new()
        {
            Id = giftCardId,
            StudioId = studio.Id,
            Code = code,
            InitialBalance = req.Amount,
            RemainingBalance = req.Amount,
            PurchaserEmail = req.PurchaserEmail,
            RecipientEmail = req.RecipientEmail,
            Status = GiftCardStatus.Pending,
            ProviderReferenceId = providerReferenceId,
            ClientSecret = clientSecret,
            Provider = "pok",
        };

        db.GiftCards.Add(giftCard);
        await db.SaveChangesAsync(ct);

        return new PurchaseGiftCardResponse(giftCard.Id, clientSecret, giftCard.Status.ToString());
    }

    // 12-char base32 (Crockford-style, no ambiguous 0/O/1/I/L) — collision-checked per studio via
    // the (StudioId, Code) unique index; regenerates on the rare collision rather than failing.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private async Task<string> GenerateUniqueCodeAsync(Guid studioId, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            char[] chars = new char[12];
            for (int i = 0; i < chars.Length; i++)
                chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            string candidate = new(chars);

            bool exists = await db.GiftCards.IgnoreQueryFilters()
                .AnyAsync(g => g.StudioId == studioId && g.Code == candidate, ct);
            if (!exists) return candidate;
        }

        throw new InvalidOperationException("Could not generate a unique gift card code.");
    }
}
