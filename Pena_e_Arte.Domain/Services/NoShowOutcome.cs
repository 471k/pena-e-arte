using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Services;

/// <summary>The payment write a no-show implies (MarkNoShowHandler performs it).</summary>
public enum NoShowPaymentAction
{
    /// <summary>The payment row is left as it is.</summary>
    None,
    /// <summary>Capture a held card deposit through the provider; the payment becomes Paid with PaidAt.</summary>
    CaptureHold,
    /// <summary>A declared-but-unreceived cash deposit is closed as nothing collected (Refunded, no PaidAt),
    /// exactly as cancellation closes it. Phi, 2026-11-05.</summary>
    CloseCashPromise,
}

/// <summary>What happens to the deposit, for the person confirming.</summary>
public enum NoShowDepositOutcome
{
    /// <summary>No money was taken (no payment, an unpaid or failed card intent): nothing moves.</summary>
    NothingCollected,
    /// <summary>A card deposit already charged: the studio keeps it, nothing moves.</summary>
    ChargedDepositKept,
    /// <summary>A cash deposit the studio already confirmed receiving: kept, nothing moves.</summary>
    CashDepositKept,
    /// <summary>A held card deposit (legacy, pre C-4): captured and kept.</summary>
    HeldDepositCaptured,
    /// <summary>Cash the client only declared and never paid: nothing was collected, so nothing is kept.</summary>
    DeclaredCashNotCollected,
    /// <summary>A payment in a state the platform can't act on (e.g. a hold with no provider reference).</summary>
    Unknown,
}

/// <param name="Amount">The payment's amount for an outcome involving money; 0 when no payment was ever
/// started. For DeclaredCashNotCollected it is the amount the client said they would pay, none of it received.</param>
public sealed record NoShowDecision(
    NoShowPaymentAction PaymentAction,
    NoShowDepositOutcome Outcome,
    decimal Amount);

/// <summary>
/// The one place that decides what marking an appointment as a no-show does to its deposit (E04b, A04).
/// Both MarkNoShowHandler (which performs the provider capture) and the no-show preview call it, the
/// way CancellationOutcome serves cancellation, so the preview and the commit cannot drift apart.
/// Pure: no database, no provider, no clock.
///
/// A declared cash deposit (CashPending) is closed as nothing collected on a no-show, the same way
/// CancellationOutcome closes it (Phi, 2026-11-05, E04b §2.1). Until then a no-show recorded it as Paid,
/// without PaidAt, although no cash was received. Production had no such rows when this changed.
/// </summary>
public static class NoShowOutcome
{
    /// <summary>The status gate, as MarkNoShowHandler has always applied it.</summary>
    public static bool IsAllowed(AppointmentStatus status) =>
        status is not (AppointmentStatus.Cancelled or AppointmentStatus.Completed);

    public static NoShowDecision Decide(Payment? payment)
    {
        if (payment is null)
            return new NoShowDecision(NoShowPaymentAction.None, NoShowDepositOutcome.NothingCollected, 0m);

        if (payment.Method == ClientPaymentMethod.Card
            && !string.IsNullOrEmpty(payment.ProviderReferenceId)
            && payment.Status == PaymentStatus.Captured)
            return new NoShowDecision(NoShowPaymentAction.CaptureHold, NoShowDepositOutcome.HeldDepositCaptured, payment.Amount);

        if (payment.Status == PaymentStatus.CashPending)
            return new NoShowDecision(NoShowPaymentAction.CloseCashPromise, NoShowDepositOutcome.DeclaredCashNotCollected, payment.Amount);

        if (payment.Status == PaymentStatus.Paid)
            return new NoShowDecision(
                NoShowPaymentAction.None,
                payment.Method == ClientPaymentMethod.Cash ? NoShowDepositOutcome.CashDepositKept : NoShowDepositOutcome.ChargedDepositKept,
                payment.Amount);

        // A hold the platform can't capture (no provider reference, or not a card): the handler leaves it,
        // and nobody can say what the studio ends up with.
        if (payment.Status == PaymentStatus.Captured)
            return new NoShowDecision(NoShowPaymentAction.None, NoShowDepositOutcome.Unknown, payment.Amount);

        // Pending or Failed card intent: no money was ever taken.
        return new NoShowDecision(NoShowPaymentAction.None, NoShowDepositOutcome.NothingCollected, 0m);
    }
}
