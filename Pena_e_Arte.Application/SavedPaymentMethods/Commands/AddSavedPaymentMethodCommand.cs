using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
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

public class AddSavedPaymentMethodHandler(IAppDbContext db, ICurrentTenant tenant, ICurrentUser currentUser, IPokCardTokenService cardTokens)
    : IRequestHandler<AddSavedPaymentMethodCommand, SavedPaymentMethodResponse>
{
    public async Task<SavedPaymentMethodResponse> Handle(AddSavedPaymentMethodCommand command, CancellationToken ct)
    {
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

    internal static SavedPaymentMethodResponse Map(SavedPaymentMethod m) => new(
        m.Id, m.CardBrand, m.MaskedPan, m.ExpiryMonth, m.ExpiryYear, m.IsDefault, m.CreatedAt);
}
