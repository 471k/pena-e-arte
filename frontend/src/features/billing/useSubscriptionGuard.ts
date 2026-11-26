import { useAppSelector } from "@/app/hooks";
import { Role } from "@/shared/types/roles";
import { useGetSubscriptionQuery } from "./billingApi";
import { useGetMyStudioQuery } from "@/features/studios/studiosApi";
import { useSupportView } from "@/shared/hooks/useSupportView";
import type { SubscriptionResponse } from "./billing.types";

const READ_ONLY_STATUSES = new Set<SubscriptionResponse["status"]>([
  "GracePeriod",
  "PastDue",
  "Cancelled",
]);

/** Why writes are off. "support": an admin's read-only support session (D05), where the server refuses every write
 * whatever the studio's state, so it takes precedence. */
export type ReadOnlyCause = "support" | "suspended" | "subscription" | null;

export function useSubscriptionGuard() {
  const role    = useAppSelector((s) => s.auth.role);
  const isOwner = role === Role.Owner;

  const { data: sub }    = useGetSubscriptionQuery(undefined, { skip: !isOwner });
  const { data: studio } = useGetMyStudioQuery(undefined,     { skip: !isOwner });

  // Only an explicit false is a suspension: a studio read that doesn't say is not evidence of one.
  const isSuspended = isOwner && studio !== undefined && studio.isActive === false;
  // Subscription state is irrelevant when suspended — suspension always takes precedence.
  const subscriptionReadOnly = !isSuspended && isOwner && !!sub && READ_ONLY_STATUSES.has(sub.status);

  const supportView = useSupportView();

  const cause: ReadOnlyCause =
    supportView          ? "support"      :
    isSuspended          ? "suspended"    :
    subscriptionReadOnly ? "subscription" :
    null;

  return { isReadOnly: cause !== null, isSuspended, cause, status: sub?.status };
}
