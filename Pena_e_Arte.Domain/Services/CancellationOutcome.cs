using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.Domain.Services;

/// <summary>
/// The money decision a cancellation makes, without performing it.
/// </summary>
/// <param name="ProviderAction">The provider call the decision implies (CancelAppointmentHandler makes it).</param>
/// <param name="RefundAmount">What goes back to the client: the released hold, the refund, or the
/// released remainder of a partial capture. 0 when nothing moves.</param>
/// <param name="KeptAmount">What the studio keeps of the money taken. 0 when nothing was taken.</param>
/// <param name="ResultingPaymentStatus">The payment's status afterwards, or null when the payment row
/// is left as it is.</param>
/// <param name="ResultingDepositStatus">The appointment's deposit status afterwards, or null when it is
/// left as it is.</param>
/// <param name="ClientOutcome">The client-facing classification.</param>
public sealed record CancellationDecision(
    CancellationProviderAction ProviderAction,
    decimal RefundAmount,
    decimal KeptAmount,
    PaymentStatus? ResultingPaymentStatus,
    DepositStatus? ResultingDepositStatus,
    CancellationClientOutcome ClientOutcome);

/// <summary>
/// The one place that decides what cancelling an appointment does with its deposit: which provider
/// action, on what base amount, rounded how, and the resulting statuses. Both CancelAppointmentHandler
/// (which performs the provider I/O) and the cancellation preview query call it, so the preview and
/// the commit cannot drift apart — a second copy of this logic in the frontend drifted once
/// (G-3/G-17, October 2026). Pure: no database, no provider, no clock.
///
/// The refund percent comes from <see cref="ClientCancellationPolicy.ResolveRefundPercent"/> for a
/// client self-cancel, and is 100 for a staff cancellation.
///
/// Base amount: always payment.Amount, the money actually taken — the authorised amount of a held
/// deposit (Captured) or the charged amount (Paid). Until 3 Nov 2026 the Paid partial refund used
/// appointment.DepositAmount instead; the two are normally equal, but when they differed that could
/// refund more than was charged (POK refuses, blocking the cancellation) or less than the policy's
/// share of what the client paid. Phi approved basing both on payment.Amount (3 Nov 2026). Rounding is
/// always <see cref="CurrencyCatalog.Round"/> in the payment's own currency, never a fixed two decimals.
/// </summary>
public static class CancellationOutcome
{
    private static readonly CancellationDecision NothingMoves = new(
        CancellationProviderAction.None, 0m, 0m, null, null, CancellationClientOutcome.NothingCollected);

    public static CancellationDecision Decide(Payment? payment, int refundPercent)
    {
        if (payment is null)
            return NothingMoves;

        bool moneyTaken = payment.Status is PaymentStatus.Captured or PaymentStatus.Paid;

        if (payment.Method == ClientPaymentMethod.Card
            && !string.IsNullOrEmpty(payment.ProviderReferenceId)
            && moneyTaken)
        {
            return payment.Status == PaymentStatus.Captured
                ? DecideHeld(payment, refundPercent)
                : DecideCharged(payment, refundPercent);
        }

        if (payment.Status == PaymentStatus.CashPending)
        {
            // Deliberately NOT subject to the refund percent: CashPending means the client only
            // declared an intent to pay cash — no money has changed hands, so there is nothing to
            // forfeit or partially refund, regardless of how much notice was given. The intent is
            // closed as Refunded.
            return new CancellationDecision(
                CancellationProviderAction.None, 0m, 0m,
                PaymentStatus.Refunded, DepositStatus.Refunded, CancellationClientOutcome.NothingCollected);
        }

        if (moneyTaken)
        {
            // Money was taken, but not in a way the platform can move: a cash deposit the studio
            // confirmed, or a card payment with no provider reference. Nothing is changed.
            return payment.Method == ClientPaymentMethod.Cash
                ? NothingMoves with { ClientOutcome = CancellationClientOutcome.SettledWithStudio }
                : NothingMoves with { ClientOutcome = CancellationClientOutcome.Unknown };
        }

        // Pending or Failed card intent (never took the client's money), or already Refunded.
        return NothingMoves;
    }

    // A held deposit (Captured = authorised, never captured) has nothing to refund: releasing the hold
    // IS the refund, and keeping part of it means capturing only that part. The refund share is rounded
    // first, exactly like the charged branch.
    private static CancellationDecision DecideHeld(Payment payment, int refundPercent)
    {
        decimal refund = refundPercent >= 100
            ? payment.Amount
            : CurrencyCatalog.Round(payment.Amount * Math.Max(refundPercent, 0) / 100m, payment.Currency);
        decimal keep = payment.Amount - refund;

        if (keep <= 0m)
        {
            return new CancellationDecision(
                CancellationProviderAction.ReleaseHold, payment.Amount, 0m,
                PaymentStatus.Refunded, DepositStatus.Refunded, CancellationClientOutcome.FullyRefunded);
        }

        if (refund <= 0m)
        {
            return new CancellationDecision(
                CancellationProviderAction.CaptureWhole, 0m, payment.Amount,
                PaymentStatus.Paid, DepositStatus.Forfeited, CancellationClientOutcome.DepositKept);
        }

        // Partial "refund" = the released remainder; the payment row records it as Refunded.
        return new CancellationDecision(
            CancellationProviderAction.CaptureKeptShare, refund, keep,
            PaymentStatus.Refunded, DepositStatus.Refunded, CancellationClientOutcome.PartiallyRefunded);
    }

    // An already-charged deposit (Paid) goes back through a real provider refund.
    private static CancellationDecision DecideCharged(Payment payment, int refundPercent)
    {
        if (refundPercent >= 100)
        {
            return new CancellationDecision(
                CancellationProviderAction.Refund, payment.Amount, 0m,
                PaymentStatus.Refunded, DepositStatus.Refunded, CancellationClientOutcome.FullyRefunded);
        }

        if (refundPercent > 0)
        {
            decimal refundAmount = CurrencyCatalog.Round(
                payment.Amount * refundPercent / 100m, payment.Currency);
            return new CancellationDecision(
                CancellationProviderAction.Refund, refundAmount, Math.Max(0m, payment.Amount - refundAmount),
                PaymentStatus.Refunded, DepositStatus.Refunded, CancellationClientOutcome.PartiallyRefunded);
        }

        // 0%: the deposit was already charged, so the studio keeps it with no provider call. The payment
        // row's status is left as it is (Paid).
        return new CancellationDecision(
            CancellationProviderAction.None, 0m, payment.Amount,
            null, DepositStatus.Forfeited, CancellationClientOutcome.DepositKept);
    }
}
