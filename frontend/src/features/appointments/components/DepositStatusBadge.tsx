import { cn } from "@/shared/utils/cn";
import { DepositStatus } from "../appointment.types";

interface DepositStatusBadgeProps {
  status: DepositStatus;
}

const DEPOSIT_STATUS_STYLES: Record<DepositStatus, string> = {
  [DepositStatus.Pending]:   "bg-yellow-100 text-yellow-800",
  [DepositStatus.Paid]:      "bg-green-100 text-green-800",
  [DepositStatus.Forfeited]: "bg-red-100 text-red-800",
  [DepositStatus.Refunded]:  "bg-blue-100 text-blue-800",
};

const DEPOSIT_STATUS_LABELS: Record<DepositStatus, string> = {
  [DepositStatus.Pending]:   "Pending",
  [DepositStatus.Paid]:      "Paid",
  [DepositStatus.Forfeited]: "Forfeited",
  [DepositStatus.Refunded]:  "Refunded",
};

export function DepositStatusBadge({ status }: DepositStatusBadgeProps) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium",
        DEPOSIT_STATUS_STYLES[status] ?? "bg-gray-100 text-gray-600"
      )}
    >
      {DEPOSIT_STATUS_LABELS[status] ?? status}
    </span>
  );
}
