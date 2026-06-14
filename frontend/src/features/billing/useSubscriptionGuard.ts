import { useAppSelector } from "@/app/hooks";
import { Role } from "@/shared/types/roles";
import { useGetSubscriptionQuery } from "./billingApi";
import { useGetMyStudioQuery } from "@/features/studios/studiosApi";
import type { SubscriptionResponse } from "./billing.types";

const READ_ONLY_STATUSES = new Set<SubscriptionResponse["status"]>([
  "GracePeriod",
  "PastDue",
  "Cancelled",
]);

export type ReadOnlyCause = "suspended" | "subscription" | null;

export function useSubscriptionGuard() {
  const role    = useAppSelector((s) => s.auth.role);
  const isOwner = role === Role.Owner;

  const { data: sub }    = useGetSubscriptionQuery(undefined, { skip: !isOwner });
  const { data: studio } = useGetMyStudioQuery(undefined,     { skip: !isOwner });

  const isSuspended = isOwner && studio !== undefined && !studio.isActive;
  // Subscription state is irrelevant when suspended — suspension always takes precedence.
  const subscriptionReadOnly = !isSuspended && isOwner && !!sub && READ_ONLY_STATUSES.has(sub.status);

  const cause: ReadOnlyCause =
    isSuspended          ? "suspended"    :
    subscriptionReadOnly ? "subscription" :
    null;

  return { isReadOnly: cause !== null, isSuspended, cause, status: sub?.status };
}
