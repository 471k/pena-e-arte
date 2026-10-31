using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// Studio-scoped prepaid balance. No ExpiresAt — balances never expire (product decision,
/// 2026-09-09). Carries its own provider fields (mirroring Payment's ProviderReferenceId /
/// ClientToken / Provider) rather than being stored as a Payment row — a gift-card purchase has
/// no appointment, and Payment.AppointmentId is non-nullable with a database-enforced
/// one-payment-per-appointment unique index. See architecture.md Decisions Log.
/// </summary>
public class GiftCard : TenantEntity, IHasReferenceAmount
{
    public string Code { get; set; } = string.Empty;
    public decimal InitialBalance { get; set; }
    public decimal RemainingBalance { get; set; }

    /// <summary>ISO 4217 currency of this gift card — copied from Studio.Currency at creation and
    /// never changed after (a record of money that moved keeps its own copy).</summary>
    public string Currency { get; set; } = string.Empty;
    public string PurchaserEmail { get; set; } = string.Empty;
    public string? RecipientEmail { get; set; }
    public GiftCardStatus Status { get; set; } = GiftCardStatus.Pending;

    /// <summary>The payment provider's own reference id for the purchase hold/capture.</summary>
    public string? ProviderReferenceId { get; set; }
    public string? ClientToken { get; set; }

    /// <summary>Which provider issued ProviderReferenceId (e.g. "pok"). Empty until confirmed.</summary>
    public string Provider { get; set; } = string.Empty;

    // Lek reference (Albanian EUR studios only): frozen when the purchase order is created and never
    // changed after. Null when none applies.
    /// <inheritdoc cref="IHasReferenceAmount.ReferenceCurrency"/>
    public string? ReferenceCurrency { get; set; }
    /// <inheritdoc cref="IHasReferenceAmount.ReferenceRate"/>
    public decimal? ReferenceRate { get; set; }
    /// <inheritdoc cref="IHasReferenceAmount.ReferenceRateDate"/>
    public DateOnly? ReferenceRateDate { get; set; }
    /// <inheritdoc cref="IHasReferenceAmount.ReferenceRateSource"/>
    public string? ReferenceRateSource { get; set; }
    /// <summary><see cref="InitialBalance"/> in whole lek at <see cref="ReferenceRate"/>.</summary>
    public decimal? ReferenceAmount { get; set; }
}
