using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Payments;

/// <summary>
/// "Is this studio's POK account connected" was independently re-implemented in
/// GetPaymentCapabilitiesQuery and GetPokConnectionStatusQuery — both need the exact same
/// Studio.PokMerchantId + StudioCredentialRef existence check. Shared here so a future change to
/// what "connected" means (e.g. a status flag beyond just "has a merchant id") only needs to
/// happen once. PokPaymentProvider.ResolveCredentialsAsync (Infrastructure) intentionally stays
/// separate — it also needs the Vault-backed secret values themselves, not just this boolean.
/// </summary>
public static class PokConnectionCheck
{
    public static async Task<(bool Connected, string? MerchantId)> ResolveAsync(
        IAppDbContext db, Guid studioId, CancellationToken ct)
    {
        Studio? studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == studioId, ct);
        bool hasCredentialRef = await db.StudioCredentialRefs
            .AnyAsync(c => c.StudioId == studioId && c.Provider == CredentialProvider.Pok, ct);

        bool connected = studio?.PokMerchantId is not null && hasCredentialRef;
        return (connected, connected ? studio!.PokMerchantId : null);
    }
}
