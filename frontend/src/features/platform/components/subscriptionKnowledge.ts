/** Statuses a cash activation may start from. Shared by the studio list, studio detail and oversight. */
export const CASH_ACTIVATABLE: ReadonlySet<string> = new Set(["NoSubscription", "PastDue", "GracePeriod", "Cancelled"]);

/**
 * What the page knows about a studio's subscription (D02). "unknown" covers both still-loading and failed:
 * neither is "No subscription", and no action that depends on the status may be offered from it.
 */
export type SubscriptionKnowledge =
  | { kind: "known"; status: string }
  | { kind: "loading" }
  | { kind: "failed" };

export function knowSubscriptionStatus(
  query: { isLoading: boolean; isError: boolean; isSuccess: boolean },
  sub: { status: string } | undefined,
): SubscriptionKnowledge {
  if (query.isError) return { kind: "failed" };
  if (!query.isSuccess) return { kind: "loading" };
  return { kind: "known", status: sub?.status ?? "NoSubscription" };
}
