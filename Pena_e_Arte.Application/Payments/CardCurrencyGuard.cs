using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments;

/// <summary>
/// Card deposits are offered only when the platform has card payments switched on (the C-13 kill
/// switch, <c>Payments:CardDepositsEnabled</c>, surfaced as Capabilities.CardPaymentsEnabled) and the
/// active provider supports the studio's currency (POK today: ALL, EUR). Every command that can
/// open a new card payment — CreateDepositPaymentCommand, PayDepositWithSavedCardCommand (via
/// ResolveOrCreateHoldAsync), CreatePaymentIntentCommand, PurchaseGiftCardCommand,
/// PurchasePackageCommand — calls this before calling the provider, so the rejection messages live
/// in exactly one place. GetPaymentCapabilitiesQuery runs the read-only version of the same checks
/// to hide the Card option client-side; this is the server-side enforcement behind that UI gate.
/// Capture, refund and release of payments already taken don't call this, so switching card
/// payments off never strands a held deposit.
/// </summary>
public static class CardCurrencyGuard
{
    public const string CardPaymentsSwitchedOffMessage =
        "Card payments aren't available right now. Please pay in cash.";

    /// <summary>The kill-switch half alone — for paths that hand out an existing provider order
    /// (resuming an unpaid one) rather than creating a new one.</summary>
    public static void EnsureCardPaymentsEnabled(IPaymentProvider provider)
    {
        if (!provider.Capabilities.CardPaymentsEnabled)
        {
            throw new BusinessRuleViolationException(CardPaymentsSwitchedOffMessage);
        }
    }

    public static void EnsureSupported(IPaymentProvider provider, string currency)
    {
        EnsureCardPaymentsEnabled(provider);

        if (!provider.Capabilities.SupportedCurrencies.Contains(currency, StringComparer.OrdinalIgnoreCase))
        {
            throw new BusinessRuleViolationException(
                $"Card payments aren't available in {currency} for this studio. Please pay in cash.");
        }
    }
}
