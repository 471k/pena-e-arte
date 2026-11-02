using FluentAssertions;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.UnitTests.Services;

/// <summary>
/// The shared money decision behind both the cancellation preview and CancelAppointmentHandler.
/// Each case pins today's handler behaviour exactly (see CancelAppointmentHandlerTests for the
/// provider-call side of the same cases).
/// </summary>
public class CancellationOutcomeTests
{
    private static Payment CardPayment(PaymentStatus status, decimal amount = 50m, string currency = "EUR",
        string? providerReferenceId = "pok_order_1") => new()
    {
        Amount = amount,
        Currency = currency,
        Status = status,
        Method = ClientPaymentMethod.Card,
        ProviderReferenceId = providerReferenceId,
    };

    private static Payment CashPayment(PaymentStatus status, decimal amount = 50m) => new()
    {
        Amount = amount,
        Currency = "EUR",
        Status = status,
        Method = ClientPaymentMethod.Cash,
    };

    // ── Held deposit (Captured) ────────────────────────────────────────────────

    [Fact]
    public void Captured_FullRefund_ReleasesTheHold()
    {
        CancellationDecision d = CancellationOutcome.Decide(CardPayment(PaymentStatus.Captured), 100);

        d.ProviderAction.Should().Be(CancellationProviderAction.ReleaseHold);
        d.RefundAmount.Should().Be(50m);
        d.KeptAmount.Should().Be(0m);
        d.ResultingPaymentStatus.Should().Be(PaymentStatus.Refunded);
        d.ResultingDepositStatus.Should().Be(DepositStatus.Refunded);
        d.ClientOutcome.Should().Be(CancellationClientOutcome.FullyRefunded);
    }

    [Fact]
    public void Captured_Partial_CapturesOnlyTheKeptShare()
    {
        CancellationDecision d = CancellationOutcome.Decide(CardPayment(PaymentStatus.Captured), 40);

        d.ProviderAction.Should().Be(CancellationProviderAction.CaptureKeptShare);
        d.RefundAmount.Should().Be(20m);
        d.KeptAmount.Should().Be(30m);
        d.ResultingPaymentStatus.Should().Be(PaymentStatus.Refunded);
        d.ResultingDepositStatus.Should().Be(DepositStatus.Refunded);
        d.ClientOutcome.Should().Be(CancellationClientOutcome.PartiallyRefunded);
    }

    [Fact]
    public void Captured_ZeroPercent_CapturesTheWholeHold()
    {
        CancellationDecision d = CancellationOutcome.Decide(CardPayment(PaymentStatus.Captured), 0);

        d.ProviderAction.Should().Be(CancellationProviderAction.CaptureWhole);
        d.RefundAmount.Should().Be(0m);
        d.KeptAmount.Should().Be(50m);
        d.ResultingPaymentStatus.Should().Be(PaymentStatus.Paid);
        d.ResultingDepositStatus.Should().Be(DepositStatus.Forfeited);
        d.ClientOutcome.Should().Be(CancellationClientOutcome.DepositKept);
    }

    [Fact]
    public void Captured_SplitsTheAuthorisedAmount()
    {
        // A held deposit's base is payment.Amount, the authorised amount.
        CancellationDecision d = CancellationOutcome.Decide(CardPayment(PaymentStatus.Captured, amount: 60m), 50);

        d.RefundAmount.Should().Be(30m);
        d.KeptAmount.Should().Be(30m);
    }

    // ── Charged deposit (Paid) ─────────────────────────────────────────────────

    [Fact]
    public void Paid_FullRefund_RefundsThePaymentAmount()
    {
        CancellationDecision d = CancellationOutcome.Decide(CardPayment(PaymentStatus.Paid), 100);

        d.ProviderAction.Should().Be(CancellationProviderAction.Refund);
        d.RefundAmount.Should().Be(50m);
        d.KeptAmount.Should().Be(0m);
        d.ResultingPaymentStatus.Should().Be(PaymentStatus.Refunded);
        d.ResultingDepositStatus.Should().Be(DepositStatus.Refunded);
        d.ClientOutcome.Should().Be(CancellationClientOutcome.FullyRefunded);
    }

    [Fact]
    public void Paid_Partial_RefundsTheShareOfWhatWasCharged()
    {
        // Since 3 Nov 2026 (Phi): the partial refund of a charged deposit is computed on payment.Amount,
        // the money taken — the appointment's DepositAmount is no longer an input at all.
        CancellationDecision d = CancellationOutcome.Decide(CardPayment(PaymentStatus.Paid), 50);

        d.ProviderAction.Should().Be(CancellationProviderAction.Refund);
        d.RefundAmount.Should().Be(25m);
        d.KeptAmount.Should().Be(25m);
        d.ResultingPaymentStatus.Should().Be(PaymentStatus.Refunded);
        d.ResultingDepositStatus.Should().Be(DepositStatus.Refunded);
        d.ClientOutcome.Should().Be(CancellationClientOutcome.PartiallyRefunded);
    }

    [Fact]
    public void Paid_ZeroPercent_KeepsTheDepositWithNoProviderCall()
    {
        CancellationDecision d = CancellationOutcome.Decide(CardPayment(PaymentStatus.Paid), 0);

        d.ProviderAction.Should().Be(CancellationProviderAction.None);
        d.RefundAmount.Should().Be(0m);
        d.KeptAmount.Should().Be(50m);
        d.ResultingPaymentStatus.Should().BeNull("the payment row stays Paid");
        d.ResultingDepositStatus.Should().Be(DepositStatus.Forfeited);
        d.ClientOutcome.Should().Be(CancellationClientOutcome.DepositKept);
    }

    // ── Nothing collected ──────────────────────────────────────────────────────

    [Fact]
    public void CashPending_AtZeroPercent_IsStillFullyRefunded()
    {
        CancellationDecision d = CancellationOutcome.Decide(CashPayment(PaymentStatus.CashPending), 0);

        d.ProviderAction.Should().Be(CancellationProviderAction.None);
        d.ResultingPaymentStatus.Should().Be(PaymentStatus.Refunded);
        d.ResultingDepositStatus.Should().Be(DepositStatus.Refunded);
        d.KeptAmount.Should().Be(0m, "nothing was collected, so nothing can be forfeited");
        d.RefundAmount.Should().Be(0m, "no money changed hands, so none moves back");
        d.ClientOutcome.Should().Be(CancellationClientOutcome.NothingCollected);
    }

    [Fact]
    public void NoPayment_NothingHappens()
    {
        CancellationDecision d = CancellationOutcome.Decide(null, 0);

        d.ProviderAction.Should().Be(CancellationProviderAction.None);
        d.RefundAmount.Should().Be(0m);
        d.KeptAmount.Should().Be(0m);
        d.ResultingPaymentStatus.Should().BeNull();
        d.ResultingDepositStatus.Should().BeNull();
        d.ClientOutcome.Should().Be(CancellationClientOutcome.NothingCollected);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Failed)]
    public void UnfinishedOrFailedCardIntent_NothingHappens(PaymentStatus status)
    {
        CancellationDecision d = CancellationOutcome.Decide(CardPayment(status), 0);

        d.ProviderAction.Should().Be(CancellationProviderAction.None);
        d.ResultingPaymentStatus.Should().BeNull();
        d.ResultingDepositStatus.Should().BeNull();
        d.KeptAmount.Should().Be(0m);
        d.ClientOutcome.Should().Be(CancellationClientOutcome.NothingCollected);
    }

    // ── Money the platform can't move ──────────────────────────────────────────

    [Fact]
    public void ConfirmedCashDeposit_IsLeftForTheStudioToSettle()
    {
        CancellationDecision d = CancellationOutcome.Decide(CashPayment(PaymentStatus.Paid), 0);

        d.ProviderAction.Should().Be(CancellationProviderAction.None);
        d.ResultingPaymentStatus.Should().BeNull();
        d.ResultingDepositStatus.Should().BeNull();
        d.ClientOutcome.Should().Be(CancellationClientOutcome.SettledWithStudio);
    }

    [Theory]
    [InlineData(PaymentStatus.Captured)]
    [InlineData(PaymentStatus.Paid)]
    public void CardMoneyWithoutAProviderReference_IsUnknown(PaymentStatus status)
    {
        CancellationDecision d = CancellationOutcome.Decide(CardPayment(status, providerReferenceId: null), 100);

        d.ProviderAction.Should().Be(CancellationProviderAction.None);
        d.ResultingPaymentStatus.Should().BeNull();
        d.ResultingDepositStatus.Should().BeNull();
        d.ClientOutcome.Should().Be(CancellationClientOutcome.Unknown);
    }

    // ── Rounding: the currency's own minor units, never a fixed two decimals ──

    [Fact]
    public void Captured_Partial_RoundsToAZeroDecimalCurrency()
    {
        // 1001 JPY × 33% = 330.33 → 330 (JPY has no minor unit); a hardcoded 2 dp would give 330.33.
        CancellationDecision d = CancellationOutcome.Decide(
            CardPayment(PaymentStatus.Captured, amount: 1001m, currency: "JPY"), 33);

        d.RefundAmount.Should().Be(330m);
        d.KeptAmount.Should().Be(671m);
    }

    [Fact]
    public void Paid_Partial_RoundsToAThreeDecimalCurrency()
    {
        // 12.345 KWD × 33% = 4.07385 → 4.074 (KWD has three); a hardcoded 2 dp would give 4.07.
        CancellationDecision d = CancellationOutcome.Decide(
            CardPayment(PaymentStatus.Paid, amount: 12.345m, currency: "KWD"), 33);

        d.RefundAmount.Should().Be(4.074m);
    }

    [Fact]
    public void Rounding_IsAwayFromZeroAtTheMidpoint()
    {
        // 25 JPY × 50% = 12.5 → 13 with away-from-zero; banker's rounding would give 12.
        CancellationDecision d = CancellationOutcome.Decide(
            CardPayment(PaymentStatus.Captured, amount: 25m, currency: "JPY"), 50);

        d.RefundAmount.Should().Be(13m);
        d.KeptAmount.Should().Be(12m);
    }
}
