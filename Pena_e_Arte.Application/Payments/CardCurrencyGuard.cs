using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments;

/// <summary>
/// Card deposits are offered only when the active provider supports the studio's currency (POK
/// today: ALL, EUR). Every command that can reach a provider — CreateDepositPaymentCommand,
/// PayDepositWithSavedCardCommand (via ResolveOrCreateHoldAsync), CreatePaymentIntentCommand,
/// PurchaseGiftCardCommand, PurchasePackageCommand — calls this before calling the provider, so
/// the rejection message and the currency check live in exactly one place. GetPaymentCapabilitiesQuery
/// runs the read-only version of this same check (currency in Capabilities.SupportedCurrencies)
/// to hide the Card option client-side; this is the server-side enforcement behind that UI gate.
/// </summary>
public static class CardCurrencyGuard
{
    public static void EnsureSupported(IPaymentProvider provider, string currency)
    {
        if (!provider.Capabilities.SupportedCurrencies.Contains(currency, StringComparer.OrdinalIgnoreCase))
        {
            throw new BusinessRuleViolationException(
                $"Card payments aren't available in {currency} for this studio. Please pay in cash.");
        }
    }
}
