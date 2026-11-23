/**
 * What the booking forms can say about a deposit BEFORE the request is saved (C02).
 *
 * The amount is the server's to decide: CreateAppointmentCommand applies a package, else the service's own
 * deposit, else the studio's active rule through DepositCalculator, and then any promo code or referral credit.
 * This is a preview of the first three steps, in that order, so the forms and the review state ONE number in the
 * same words. It is always presented as an estimate, and the amount the client is asked to pay is the one the
 * server returns after saving (booked.depositAmount), never this one.
 *
 * It replaces two identical copies that lived inside each form's deposit preview. One difference from those
 * copies, on purpose: a percentage rule with an artist who has no rate is "none", because DepositCalculator
 * returns 0 for it (`artistHourlyRate is > 0m`), where the old copy said "set once an artist is picked" about an
 * artist who was already picked.
 */
export type DepositEstimate =
  /** A package session pays for the booking; the server zeroes the deposit. */
  | { kind: "package" }
  /** A figure, from the service's own deposit or the studio's rule. Still an estimate: codes and credit come after. */
  | { kind: "amount"; amount: number; basis: "service" | "rule" }
  /** A percentage rule with no artist yet: the server works it out from the artist's rate once one is set. */
  | { kind: "awaiting-artist"; studioAssigns: boolean }
  /** No deposit: no active rule, a zero amount, or a percentage of an artist with no rate. */
  | { kind: "none" };

export interface DepositRuleShape {
  amountFixed:   number | null;
  amountPercent: number | null;
}

export interface DepositEstimateInput {
  packageCovered:  boolean;
  /** The picked service's own DepositAmount, or null when there is no service or it has none. */
  serviceDeposit:  number | null;
  /** The studio's active rule, or null when there is none. */
  rule:            DepositRuleShape | null;
  /** True once a specific artist is chosen; false while none is, or when the studio assigns one. */
  artistChosen:    boolean;
  studioAssigns:   boolean;
  hourlyRate:      number | null;
  durationMinutes: number;
}

export function estimateDeposit(input: DepositEstimateInput): DepositEstimate {
  if (input.packageCovered) return { kind: "package" };
  if (input.serviceDeposit !== null) {
    return input.serviceDeposit > 0 ? { kind: "amount", amount: input.serviceDeposit, basis: "service" } : { kind: "none" };
  }
  const rule = input.rule;
  if (rule === null) return { kind: "none" };
  if (rule.amountFixed !== null) {
    return rule.amountFixed > 0 ? { kind: "amount", amount: rule.amountFixed, basis: "rule" } : { kind: "none" };
  }
  if (rule.amountPercent === null) return { kind: "none" };
  if (!input.artistChosen) return { kind: "awaiting-artist", studioAssigns: input.studioAssigns };
  if (input.hourlyRate === null || input.hourlyRate <= 0) return { kind: "none" };
  const amount = (input.durationMinutes / 60) * input.hourlyRate * rule.amountPercent / 100;
  return amount > 0 ? { kind: "amount", amount, basis: "rule" } : { kind: "none" };
}
