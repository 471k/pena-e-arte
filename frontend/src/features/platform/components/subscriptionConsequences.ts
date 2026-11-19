import { formatCurrency } from "@/shared/utils/formatCurrency";
import type { AdminCancellationQuoteResponse, CancelRefundChoice } from "@/features/platform/platform.types";

// Pure rules behind the admin consequence reviews (slice 8 phase 3, D03 and D07), kept apart from the components so
// each can be tested on its own and the component files only export components.

export const DAY_MS = 24 * 60 * 60 * 1000;

/** Instants on admin consequence reviews are shown in UTC and say so: an admin may be in any zone. */
export function formatUtc(iso: string | Date): string {
  const date = typeof iso === "string" ? new Date(iso) : iso;
  return `${date.toLocaleString("en-GB", {
    day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit", timeZone: "UTC",
  })} UTC`;
}

/** What the chosen rule refunds. With no refund applicable every rule refunds nothing (the server's own rule). */
export function refundFor(quote: AdminCancellationQuoteResponse, choice: CancelRefundChoice): number {
  if (!quote.refundApplies) return 0;
  if (choice === "AdminFull") return quote.fullRefund;
  if (choice === "AdminNone") return 0;
  return quote.formulaRefund;
}

/** ExtendTrialHandler's rule: from the current end while the trial is running, from now once it has ended. */
export function extendedTrialEnd(currentEnd: string | null, days: number, now: Date): Date {
  const current = currentEnd ? new Date(currentEnd) : null;
  const base = current && current > now ? current : now;
  return new Date(base.getTime() + days * DAY_MS);
}

/** A refund's amount in its own currency (evidence §0.3: it used to be a hardcoded euro). */
export function refundAmount(amount: number, currency: string): string {
  return formatCurrency(amount, currency.toUpperCase());
}

/**
 * What to do about a failed refund (D07). Two different situations, so two different next actions:
 * - It reached Stripe (a refund id exists) and Stripe later reported it failed: the money went back to the platform's
 *   Stripe balance and the studio hasn't got it.
 * - The request itself failed: Stripe may or may not have made the refund (a timeout can follow a refund that went
 *   through), so the first step is to look, not to refund again.
 * Neither updates the row afterwards: there is no retry or "resolved" action yet (that is a product decision).
 */
export function nextActionFor(reachedStripe: boolean, invoiceReference: string): string {
  return reachedStripe
    ? `Stripe reported this refund failed, so the money went back to the platform's Stripe balance and the studio hasn't received it. Refund it again from invoice ${invoiceReference} in Stripe, or pay the studio back another way. This entry stays as it is afterwards.`
    : `The refund request didn't go through, so Stripe may not have made it. Check invoice ${invoiceReference} in Stripe first: if a refund is there, nothing more is needed; if not, refund it there. This entry stays as it is afterwards.`;
}
