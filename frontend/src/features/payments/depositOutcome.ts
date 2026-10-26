import { formatCurrency, formatWithReference, toWholeLek } from "@/shared/utils/formatCurrency";
import { PaymentMethod, PaymentStatus, type PaymentResponse } from "./payment.types";

/**
 * What actually happened to a deposit, in plain words for the person reading it (G-13, agreed
 * with Phi 2026-10-16). The API keeps one Status for several outcomes — "Refunded" covers a
 * full refund, a partial refund, a released hold and a late-cancel partial capture — so the
 * outcome is worked out from the amounts: refundedAmount (given back), paidAt (was anything
 * charged at all) and the appointment's status (a cancelled appointment means the studio kept
 * part under its cancellation policy, rather than refunding part of a charge).
 */
export type DepositOutcomeKind =
  | "pending" | "cashPending" | "onHold" | "paid" | "kept"
  | "partlyRefunded" | "partlyKept" | "refunded" | "released" | "failed";

export interface DepositOutcome {
  kind: DepositOutcomeKind;
  ownerLabel: string;
  ownerDetail: string | null;
  clientLabel: string;
  clientDetail: string | null;
  /** Money actually left the client's card / was collected — only then is there a receipt (G-6). */
  charged: boolean;
}

type DepositFields = Pick<PaymentResponse,
  "amount" | "status" | "method" | "paidAt" | "currency" | "refundedAmount" | "appointmentStatus"
  | "referenceRate" | "referenceCurrency">;

export function describeDeposit(p: DepositFields): DepositOutcome {
  const money = (n: number) => formatCurrency(n, p.currency);
  // Client strings only (owner strings stay in the studio's currency, spec Q3): for Albanian EUR
  // studios each amount also shows its lek at the rate frozen when the client paid — never a later
  // rate, also for refunds (spec D5).
  const clientMoney = (n: number) =>
    p.referenceRate != null && p.referenceCurrency
      ? formatWithReference(n, p.currency,
          { amount: toWholeLek(n, p.referenceRate), currency: p.referenceCurrency, approximate: false })
      : money(n);
  const isCash = p.method === PaymentMethod.Cash;
  const cancelled = p.appointmentStatus === "Cancelled";

  switch (p.status) {
    case PaymentStatus.Pending:
      return { kind: "pending", ownerLabel: "Pending", ownerDetail: "Waiting for the client to pay",
        clientLabel: "Deposit not paid yet", clientDetail: null, charged: false };
    case PaymentStatus.CashPending:
      return { kind: "cashPending", ownerLabel: "Cash pending", ownerDetail: `${money(p.amount)} to collect in cash`,
        clientLabel: "Paying in cash at the studio", clientDetail: null, charged: false };
    case PaymentStatus.Failed:
      // Also what a held deposit becomes when POK ends the hold before it is charged (C-15).
      return { kind: "failed", ownerLabel: "Failed",
        ownerDetail: "Nothing was charged: the card payment didn't go through, or its hold ended",
        clientLabel: "Payment didn't go through", clientDetail: null, charged: false };
    case PaymentStatus.Captured:
      return { kind: "onHold", ownerLabel: "On hold",
        ownerDetail: `${money(p.amount)} reserved on the client's card, not charged yet`,
        clientLabel: "Deposit on hold", clientDetail: "Charged by the studio, usually at your appointment",
        charged: false };
    case PaymentStatus.Paid:
      if (cancelled) {
        return { kind: "kept", ownerLabel: "Kept", ownerDetail: `${money(p.amount)} kept (cancellation policy)`,
          clientLabel: "Deposit kept", clientDetail: "Kept under the studio's cancellation policy", charged: true };
      }
      return { kind: "paid", ownerLabel: "Paid", ownerDetail: `${money(p.amount)} received`,
        clientLabel: "Deposit paid", clientDetail: null, charged: true };
    case PaymentStatus.Refunded:
    default: {
      const givenBack = Math.min(p.refundedAmount ?? p.amount, p.amount);
      const kept = Math.max(p.amount - givenBack, 0);
      if (!p.paidAt) {
        return { kind: "released", ownerLabel: "Released",
          ownerDetail: isCash ? "Nothing was collected" : "Nothing was charged",
          clientLabel: isCash ? "Deposit cancelled" : "Hold released",
          clientDetail: isCash ? "Nothing was collected" : "Nothing was charged", charged: false };
      }
      if (kept <= 0) {
        return { kind: "refunded", ownerLabel: "Refunded", ownerDetail: `${money(givenBack)} refunded to the client`,
          clientLabel: "Deposit refunded", clientDetail: `${clientMoney(givenBack)} refunded to your card`, charged: true };
      }
      if (cancelled) {
        // Card deposits are charged when the client pays (C-4, 2026-10-22), so the part the policy
        // doesn't keep goes back to the card as a refund. (A hold from before then was partly
        // captured instead, and the rest released; "refunded" covers both for the client.)
        return { kind: "partlyKept", ownerLabel: "Partly kept",
          ownerDetail: `${money(kept)} kept · ${money(givenBack)} refunded (cancellation policy)`,
          clientLabel: "Partly refunded",
          clientDetail: `${clientMoney(kept)} kept under the studio's cancellation policy, ${clientMoney(givenBack)} refunded to your card`,
          charged: true };
      }
      return { kind: "partlyRefunded", ownerLabel: "Partly refunded",
        ownerDetail: `${money(kept)} kept · ${money(givenBack)} refunded`,
        clientLabel: "Partly refunded", clientDetail: `${clientMoney(givenBack)} refunded to your card`, charged: true };
    }
  }
}

// Badge colours by outcome (G-13): green = studio received money, slate = given back / nothing charged.
// The -300 shades keep the label readable on the dark theme.
export const OUTCOME_BADGE_CLASSES: Record<DepositOutcomeKind, string> = {
  pending:        "bg-yellow-500/10 text-yellow-700 dark:text-yellow-300",
  cashPending:    "bg-orange-500/10 text-orange-700 dark:text-orange-300",
  onHold:         "bg-blue-500/10   text-blue-700 dark:text-blue-300",
  paid:           "bg-green-500/10  text-green-700 dark:text-green-300",
  kept:           "bg-green-500/10  text-green-700 dark:text-green-300",
  partlyKept:     "bg-green-500/10  text-green-700 dark:text-green-300",
  partlyRefunded: "bg-slate-500/10  text-slate-700 dark:text-slate-300",
  refunded:       "bg-slate-500/10  text-slate-700 dark:text-slate-300",
  released:       "bg-slate-500/10  text-slate-700 dark:text-slate-300",
  failed:         "bg-red-500/10    text-red-700 dark:text-red-300",
};
