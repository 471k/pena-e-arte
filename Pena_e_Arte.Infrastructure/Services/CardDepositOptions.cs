namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// <c>Payments:CardDepositsEnabled</c> — the kill switch for card payments (go-live blocker C-13 in
/// docs/specs/pok-flow-a-go-live-blockers-2026-10-07.md). Off unless configured: production and
/// staging leave it unset until Phi approves card deposits there; the local stack turns it on.
/// PokPaymentProvider reports it as <c>PaymentProviderCapabilities.CardPaymentsEnabled</c>.
/// </summary>
public sealed class CardDepositOptions
{
    public const string Section = "Payments";

    public bool CardDepositsEnabled { get; init; }
}
