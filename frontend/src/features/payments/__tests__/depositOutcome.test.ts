import { describe, expect, it } from "vitest";
import { describeDeposit, type DepositOutcome } from "../depositOutcome";
import { PaymentMethod, PaymentStatus, type PaymentResponse } from "../payment.types";

type Fields = Parameters<typeof describeDeposit>[0];

// Intl puts a no-break space between "ALL" and the number; compare as plain text.
function plainOutcome(f: Fields): DepositOutcome {
  const o = describeDeposit(f);
  const plain = (t: string | null): string | null => t?.replaceAll(String.fromCharCode(160), " ") ?? null;
  return { ...o, ownerDetail: plain(o.ownerDetail), clientDetail: plain(o.clientDetail) };
}

// The 2026-10-15 re-test amounts: a 1,234 ALL deposit.
function payment(overrides: Partial<Fields>): Fields {
  const base: Pick<PaymentResponse,
    "amount" | "status" | "method" | "paidAt" | "currency" | "refundedAmount" | "appointmentStatus"> = {
    amount: 1234,
    status: PaymentStatus.Paid,
    method: PaymentMethod.Card,
    paidAt: "2026-10-15T10:00:00Z",
    currency: "ALL",
    refundedAmount: null,
    appointmentStatus: "Completed",
  };
  return { ...base, ...overrides };
}

describe("describeDeposit (G-13)", () => {
  it("a held card deposit is 'On hold', not charged, no receipt", () => {
    const o = plainOutcome(payment({ status: PaymentStatus.Captured, paidAt: null, appointmentStatus: "Confirmed" }));
    expect(o.kind).toBe("onHold");
    expect(o.ownerLabel).toBe("On hold");
    expect(o.ownerDetail).toBe("ALL 1,234 reserved on the client's card, not charged yet");
    expect(o.clientLabel).toBe("Deposit on hold");
    expect(o.clientDetail).toBe("Charged by the studio, usually at your appointment");
    expect(o.charged).toBe(false);
  });

  it("a charged deposit is 'Paid' with the amount received", () => {
    const o = plainOutcome(payment({}));
    expect(o.kind).toBe("paid");
    expect(o.ownerDetail).toBe("ALL 1,234 received");
    expect(o.clientLabel).toBe("Deposit paid");
    expect(o.charged).toBe(true);
  });

  it("a deposit kept in full on a late cancel is 'Kept', not 'Paid'", () => {
    const o = plainOutcome(payment({ appointmentStatus: "Cancelled" }));
    expect(o.kind).toBe("kept");
    expect(o.ownerDetail).toBe("ALL 1,234 kept (cancellation policy)");
    expect(o.clientLabel).toBe("Deposit kept");
    expect(o.charged).toBe(true);
  });

  it("a partial refund (G-5) shows what was kept and what was given back", () => {
    const o = plainOutcome(payment({ status: PaymentStatus.Refunded, refundedAmount: 234 }));
    expect(o.kind).toBe("partlyRefunded");
    expect(o.ownerLabel).toBe("Partly refunded");
    expect(o.ownerDetail).toBe("ALL 1,000 kept · ALL 234 refunded");
    expect(o.clientDetail).toBe("ALL 234 refunded to your card");
    expect(o.charged).toBe(true);
  });

  it("a full refund is 'Refunded' and still has a receipt (money was charged first)", () => {
    const o = plainOutcome(payment({ status: PaymentStatus.Refunded, refundedAmount: 1234 }));
    expect(o.kind).toBe("refunded");
    expect(o.ownerDetail).toBe("ALL 1,234 refunded to the client");
    expect(o.clientLabel).toBe("Deposit refunded");
    expect(o.charged).toBe(true);
  });

  it("an older refund row with no refundedAmount is treated as a full refund", () => {
    expect(plainOutcome(payment({ status: PaymentStatus.Refunded, refundedAmount: null })).kind).toBe("refunded");
  });

  it("a released hold (G-6) is 'Released', nothing charged, no receipt", () => {
    const o = plainOutcome(payment({
      status: PaymentStatus.Refunded, paidAt: null, refundedAmount: 1234, appointmentStatus: "Cancelled",
    }));
    expect(o.kind).toBe("released");
    expect(o.ownerLabel).toBe("Released");
    expect(o.ownerDetail).toBe("Nothing was charged");
    expect(o.clientLabel).toBe("Hold released");
    expect(o.charged).toBe(false);
  });

  it("a cancelled cash deposit says nothing was collected", () => {
    const o = plainOutcome(payment({
      status: PaymentStatus.Refunded, method: PaymentMethod.Cash, paidAt: null, appointmentStatus: "Cancelled",
    }));
    expect(o.kind).toBe("released");
    expect(o.ownerDetail).toBe("Nothing was collected");
    expect(o.clientLabel).toBe("Deposit cancelled");
  });

  it("a late-cancel partial capture is 'Partly kept' with the policy split", () => {
    const o = plainOutcome(payment({
      status: PaymentStatus.Refunded, amount: 1000, refundedAmount: 500, appointmentStatus: "Cancelled",
    }));
    expect(o.kind).toBe("partlyKept");
    expect(o.ownerLabel).toBe("Partly kept");
    expect(o.ownerDetail).toBe("ALL 500 kept · ALL 500 refunded (cancellation policy)");
    expect(o.clientLabel).toBe("Partly refunded");
    expect(o.clientDetail).toBe("ALL 500 kept under the studio's cancellation policy, ALL 500 refunded to your card");
    expect(o.charged).toBe(true);
  });

  it("a refunded amount above the deposit never shows a negative 'kept'", () => {
    const o = plainOutcome(payment({ status: PaymentStatus.Refunded, refundedAmount: 2000 }));
    expect(o.kind).toBe("refunded");
    expect(o.ownerDetail).toBe("ALL 1,234 refunded to the client");
  });

  it.each([
    [PaymentStatus.Pending, "pending", "Deposit not paid yet"],
    [PaymentStatus.CashPending, "cashPending", "Paying in cash at the studio"],
    [PaymentStatus.Failed, "failed", "Payment didn't go through"],
  ] as const)("%s is not charged", (status, kind, clientLabel) => {
    const o = plainOutcome(payment({ status, paidAt: null }));
    expect(o.kind).toBe(kind);
    expect(o.clientLabel).toBe(clientLabel);
    expect(o.charged).toBe(false);
  });
});

describe("describeDeposit — lek at the rate frozen when the client paid (Albanian EUR studios)", () => {
  const euroPaid = (overrides: Partial<Fields>): Fields =>
    payment({ amount: 100, currency: "EUR", referenceRate: 90.74, referenceCurrency: "ALL", ...overrides });

  it("a full refund shows the refunded euros with their lek at the original rate", () => {
    const o = plainOutcome(euroPaid({ status: PaymentStatus.Refunded, refundedAmount: 100 }));
    expect(o.clientDetail).toBe("€100 (ALL 9,074) refunded to your card");
  });

  it("a partial refund converts only the refunded part, at the original rate (D5)", () => {
    // 30 EUR x 90.74 = 2,722.2 -> 2,722, whatever today's rate is.
    const o = plainOutcome(euroPaid({ status: PaymentStatus.Refunded, refundedAmount: 30 }));
    expect(o.clientDetail).toBe("€30 (ALL 2,722) refunded to your card");
  });

  it("a late-cancel partial keep converts each part on its own", () => {
    const o = plainOutcome(euroPaid({
      status: PaymentStatus.Refunded, refundedAmount: 60, appointmentStatus: "Cancelled",
    }));
    expect(o.clientDetail).toBe(
      "€40 (ALL 3,630) kept under the studio's cancellation policy, €60 (ALL 5,444) refunded to your card");
  });

  it("owner strings show the lek at the original rate too (Q3, decided 2026-10-26)", () => {
    const o = plainOutcome(euroPaid({ status: PaymentStatus.Refunded, refundedAmount: 30 }));
    expect(o.ownerDetail).toBe("€70 (ALL 6,352) kept · €30 (ALL 2,722) refunded");
  });

  it("without a frozen reference the client text is exactly as before", () => {
    const o = plainOutcome(payment({ amount: 100, currency: "EUR", status: PaymentStatus.Refunded, refundedAmount: 30 }));
    expect(o.clientDetail).toBe("€30 refunded to your card");
  });
});
