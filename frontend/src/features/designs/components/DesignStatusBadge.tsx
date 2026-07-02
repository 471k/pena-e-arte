import { cn } from "@/shared/utils/cn";
import type { DesignStatus } from "../design.types";

const STATUS_LABEL: Record<DesignStatus, string> = {
  Draft:             "Draft",
  InReview:          "In Review",
  Approved:          "Approved",
  ChangesRequested:  "Changes Requested",
};

const STATUS_CLS: Record<DesignStatus, string> = {
  Draft:             "bg-muted text-muted-foreground",
  InReview:          "bg-blue-500/15 text-blue-600 dark:text-blue-400",
  Approved:          "bg-green-500/15 text-green-600 dark:text-green-400",
  ChangesRequested:  "bg-amber-500/15 text-amber-700 dark:text-amber-400",
};

export function DesignStatusBadge({ status }: { status: DesignStatus }) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium shrink-0",
        STATUS_CLS[status],
      )}
    >
      {STATUS_LABEL[status]}
    </span>
  );
}
