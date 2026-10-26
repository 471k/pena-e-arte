using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// Freezes the lek reference onto a client money record at the moment the client pays (spec D3): when
/// the provider order is created for a card payment, or when the studio confirms cash. Only for studios
/// in Albania that price in EUR; every other studio's record is left with no reference (D9). Never
/// throws for a missing rate: the payment must not fail because of a display feature.
/// </summary>
public interface IReferenceAmountSnapshotter
{
    /// <param name="record">The Payment, GiftCard or PackagePurchase about to be saved.</param>
    /// <param name="studioCountryCode">Studio.CountryCode.</param>
    /// <param name="currency">The record's own currency (copied from Studio.Currency).</param>
    /// <param name="amount">The record's amount in that currency.</param>
    Task FreezeAsync(IHasReferenceAmount record, string studioCountryCode, string currency, decimal amount, CancellationToken ct);
}
