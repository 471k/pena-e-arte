import type { ReactNode } from "react";
import { Money } from "@/shared/components/Money";
import { NoShowOutcome } from "../appointment.types";
import type { NoShowResultResponse } from "../appointment.types";

// The words for each no-show outcome (E04b, A04). The outcome, amount and currency all come from the
// server (NoShowOutcome); this file only words them. Shared by the confirmation dialog and its tests.

function money(amount: number, currency: string): ReactNode {
  return <Money amount={amount} currency={currency} />;
}

/** Before committing: what will happen. */
export function previewCopy(outcome: NoShowOutcome, amount: number, currency: string): ReactNode {
  switch (outcome) {
    case NoShowOutcome.NothingCollected:
      return <>No deposit was paid, so no money moves. The booking&apos;s deposit will be recorded as kept.</>;
    case NoShowOutcome.ChargedDepositKept:
      return <>The studio keeps the {money(amount, currency)} deposit the client already paid by card. Nothing is refunded.</>;
    case NoShowOutcome.CashDepositKept:
      return <>The studio keeps the {money(amount, currency)} cash deposit it received. Nothing is refunded.</>;
    case NoShowOutcome.HeldDepositCaptured:
      return <>The {money(amount, currency)} held on the client&apos;s card will be charged now and kept.</>;
    case NoShowOutcome.DeclaredCashNotCollected:
      return (
        <>
          The client said they would pay {money(amount, currency)} in cash, but no cash was received, so there&apos;s
          nothing to keep. The cash deposit is recorded as not collected.
        </>
      );
    case NoShowOutcome.Unknown:
    default:
      return null;
  }
}

/** After committing: what the server says it did. */
export function resultCopy(result: NoShowResultResponse): ReactNode {
  const amt = money(result.amount, result.currency);
  switch (result.outcome) {
    case NoShowOutcome.NothingCollected:
      return <>Marked as a no-show. No money moved.</>;
    case NoShowOutcome.ChargedDepositKept:
    case NoShowOutcome.CashDepositKept:
      return <>Marked as a no-show. The studio kept the {amt} deposit.</>;
    case NoShowOutcome.HeldDepositCaptured:
      return <>Marked as a no-show. {amt} was charged to the client&apos;s card and kept.</>;
    case NoShowOutcome.DeclaredCashNotCollected:
      return <>Marked as a no-show. No cash was received, so the {amt} cash deposit is recorded as not collected.</>;
    case NoShowOutcome.Unknown:
    default:
      return <>Marked as a no-show. What happened to the deposit isn&apos;t clear, so check the payment.</>;
  }
}
