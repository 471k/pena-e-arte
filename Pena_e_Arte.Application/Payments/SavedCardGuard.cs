using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments;

/// <summary>
/// Saved cards are offered only when the platform has them switched on (<c>Payments:SavedCardsEnabled</c>,
/// surfaced as Capabilities.SavedCardsEnabled, which PokPaymentProvider reports only while card deposits
/// are on too). Off everywhere until the tokenisation calls match POK's documented API (go-live backlog
/// C-9). AddSavedPaymentMethodCommand and PayDepositWithSavedCardCommand call this first, before anything
/// reaches POK; listing and removing saved cards don't, so a client can always delete what is stored.
/// GetPaymentCapabilitiesQuery reports the same switch as SavedCardsAvailable to hide the UI.
/// </summary>
public static class SavedCardGuard
{
    public const string SavedCardsSwitchedOffMessage =
        "Saved cards aren't available right now. Please pay with a new card or in cash.";

    public static void EnsureSavedCardsEnabled(IPaymentProvider provider)
    {
        if (!provider.Capabilities.SavedCardsEnabled)
        {
            throw new BusinessRuleViolationException(SavedCardsSwitchedOffMessage);
        }
    }
}
