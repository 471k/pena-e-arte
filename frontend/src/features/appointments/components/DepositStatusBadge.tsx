import { Badge } from "@/shared/components/ui/badge";
import { cn } from "@/shared/utils/cn";
import { DepositStatus } from "../appointment.types";

interface DepositStatusBadgeProps {
  status: DepositStatus;
}

const DEPOSIT_STATUS_STYLES: Record<DepositStatus, string> = {
  [DepositStatus.Pending]:   "border-yellow-300 bg-yellow-100 text-yellow-800 hover:bg-yellow-100",
  [DepositStatus.Paid]:      "border-green-300 bg-green-100 text-green-800 hover:bg-green-100",
  [DepositStatus.Forfeited]: "border-red-300 bg-red-100 text-red-800 hover:bg-red-100",
  [DepositStatus.Refunded]:  "border-blue-300 bg-blue-100 text-blue-800 hover:bg-blue-100",
};

const DEPOSIT_STATUS_LABELS: Record<DepositStatus, string> = {
  [DepositStatus.Pending]:   "Pending",
  [DepositStatus.Paid]:      "Paid",
  [DepositStatus.Forfeited]: "Forfeited",
  [DepositStatus.Refunded]:  "Refunded",
};

export function DepositStatusBadge({ status }: DepositStatusBadgeProps) {
  return (
    <Badge
      variant="outline"
      className={cn(DEPOSIT_STATUS_STYLES[status] ?? "border-gray-300 bg-gray-100 text-gray-600")}
    >
      {DEPOSIT_STATUS_LABELS[status] ?? status}
    </Badge>
  );
}
