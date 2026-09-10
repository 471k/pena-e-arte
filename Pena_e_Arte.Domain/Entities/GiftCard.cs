using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// Studio-scoped prepaid balance. No ExpiresAt — balances never expire (product decision,
/// 2026-09-09). Carries its own provider fields (mirroring Payment's ProviderReferenceId /
/// ClientSecret / Provider) rather than being stored as a Payment row — a gift-card purchase has
/// no appointment, and Payment.AppointmentId is non-nullable with a database-enforced
/// one-payment-per-appointment unique index. See architecture.md Decisions Log.
/// </summary>
public class GiftCard : TenantEntity
{
    public string Code { get; set; } = string.Empty;
    public decimal InitialBalance { get; set; }
    public decimal RemainingBalance { get; set; }
    public string PurchaserEmail { get; set; } = string.Empty;
    public string? RecipientEmail { get; set; }
    public GiftCardStatus Status { get; set; } = GiftCardStatus.Pending;

    /// <summary>The payment provider's own reference id for the purchase hold/capture.</summary>
    public string? ProviderReferenceId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>Which provider issued ProviderReferenceId (e.g. "pok"). Empty until confirmed.</summary>
    public string Provider { get; set; } = string.Empty;
}
