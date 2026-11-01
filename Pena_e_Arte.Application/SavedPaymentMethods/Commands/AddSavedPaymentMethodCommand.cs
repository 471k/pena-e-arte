using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Payments;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.SavedPaymentMethods.Commands;

/// <summary>Exchanges AddCardForm's JWE for a POK card token and saves it for the caller's own
/// client record at the current studio. The JWE itself is never persisted — only what
/// PokCardTokenService.TokenizeCardAsync returns.</summary>
public record AddSavedPaymentMethodCommand(AddSavedPaymentMethodRequest Request) : IRequest<SavedPaymentMethodResponse>;

public class AddSavedPaymentMethodHandler(
    IAppDbContext db, ICurrentTenant tenant, ICurrentUser currentUser, IPokCardTokenService cardTokens,
    IPaymentProvider paymentProvider)
    : IRequestHandler<AddSavedPaymentMethodCommand, SavedPaymentMethodResponse>
{
    public async Task<SavedPaymentMethodResponse> Handle(AddSavedPaymentMethodCommand command, CancellationToken ct)
    {
        // Before anything else, so nothing reaches POK's tokenisation while saved cards are off (C-9).
        SavedCardGuard.EnsureSavedCardsEnabled(paymentProvider);

        AddSavedPaymentMethodRequest req = command.Request;

        Client client = await db.FindClientForUserAsync(currentUser, ct)
            ?? throw new NotFoundException(nameof(Client), currentUser.UserId);

        PokTokenizedCard tokenized = await cardTokens.TokenizeCardAsync(
            tenant.StudioId, req.Jwe, req.SecurityCode,
            new PokCardBillingInfo(
                req.FirstName, req.LastName, req.Email, req.CountryCode,
                req.AdministrativeArea, req.Locality, req.Address1, req.PostalCode, req.PhoneNumber),
            ct);

        // Idempotent add-retry: a retried AddCardForm submit for a card already on file
        // resolves to the existing row (refreshing its display metadata) instead of a
        // duplicate — the unique index would reject a blind insert anyway.
        SavedPaymentMethod? existing = await db.SavedPaymentMethods
            .FirstOrDefaultAsync(s => s.ClientId == client.Id && s.ProviderCardTokenId == tokenized.CardTokenId, ct);

        if (existing is not null)
        {
            existing.CardBrand = tokenized.Brand;
            existing.MaskedPan = tokenized.MaskedPan;
            existing.ExpiryMonth = tokenized.ExpiryMonth;
            existing.ExpiryYear = tokenized.ExpiryYear;
            existing.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Map(existing);
        }

        // The same card saved again: POK hands out a NEW card id every time and offers no card
        // fingerprint (POK email Q16), so the token check above can't catch it. Match on what POK
        // does give — brand + last four — and keep one row per card, now pointing at the newest
        // token (POK keeps no expiry, so the newest token is the one most likely to stay valid).
        // Cards whose brand or number POK didn't return are never merged.
        SavedPaymentMethod? sameCard = await FindSameCardAsync(client.Id, tokenized, ct);
        if (sameCard is not null)
        {
            sameCard.ProviderCardTokenId = tokenized.CardTokenId;
            sameCard.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Map(sameCard) with { AlreadySaved = true };
        }

        bool isFirst = !await db.SavedPaymentMethods.AnyAsync(s => s.ClientId == client.Id, ct);

        SavedPaymentMethod method = new()
        {
            StudioId = tenant.StudioId,
            ClientId = client.Id,
            Provider = "pok",
            ProviderCardTokenId = tokenized.CardTokenId,
            CardBrand = tokenized.Brand,
            MaskedPan = tokenized.MaskedPan,
            ExpiryMonth = tokenized.ExpiryMonth,
            ExpiryYear = tokenized.ExpiryYear,
            IsDefault = isFirst,
        };

        db.SavedPaymentMethods.Add(method);
        await db.SaveChangesAsync(ct);

        return Map(method);
    }

    private async Task<SavedPaymentMethod?> FindSameCardAsync(Guid clientId, PokTokenizedCard card, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(card.Brand) || string.IsNullOrWhiteSpace(card.MaskedPan))
            return null;

        List<SavedPaymentMethod> candidates = await db.SavedPaymentMethods
            .Where(s => s.ClientId == clientId && s.MaskedPan == card.MaskedPan && s.CardBrand != null)
            .OrderByDescending(s => s.IsDefault)
            .ThenBy(s => s.CreatedAt)
            .ToListAsync(ct);

        // Brand casing has varied between POK responses ("VISA", "Visa").
        return candidates.FirstOrDefault(s => string.Equals(s.CardBrand, card.Brand, StringComparison.OrdinalIgnoreCase));
    }

    internal static SavedPaymentMethodResponse Map(SavedPaymentMethod m) => new(
        m.Id, m.CardBrand, m.MaskedPan, m.ExpiryMonth, m.ExpiryYear, m.IsDefault, m.CreatedAt);
}
