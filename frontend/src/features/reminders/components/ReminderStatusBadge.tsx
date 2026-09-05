import { Badge } from "@/shared/components/ui/badge";
import { cn } from "@/shared/utils/cn";
import type { ManualReminderStatus } from "../reminder.types";

const STATUS_STYLES: Record<ManualReminderStatus, string> = {
  Scheduled: "border-yellow-300 bg-yellow-100 text-yellow-800 hover:bg-yellow-100",
  Sent:      "border-green-300 bg-green-100 text-green-800 hover:bg-green-100",
  Failed:    "border-red-300 bg-red-100 text-red-800 hover:bg-red-100",
  Cancelled: "border-gray-300 bg-gray-100 text-gray-600 hover:bg-gray-100",
};

export function ReminderStatusBadge({ status }: { status: ManualReminderStatus }) {
  return (
    <Badge variant="outline" className={cn(STATUS_STYLES[status] ?? STATUS_STYLES.Cancelled)}>
      {status}
    </Badge>
  );
}
