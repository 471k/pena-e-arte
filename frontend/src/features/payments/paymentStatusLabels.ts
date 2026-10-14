import { PaymentStatus } from "./payment.types";

/** Owner-facing labels. The API's "Captured" means authorised-and-held, NOT charged — POK's own
 * merchant dashboard calls this state "On hold", so owners see the same words in both places. */
export const PAYMENT_STATUS_LABELS: Record<PaymentStatus, string> = {
  [PaymentStatus.Pending]: "Pending",
  [PaymentStatus.CashPending]: "Cash Pending",
  [PaymentStatus.Captured]: "On hold",
  [PaymentStatus.Paid]: "Paid",
  [PaymentStatus.Refunded]: "Refunded",
  [PaymentStatus.Failed]: "Failed",
};
