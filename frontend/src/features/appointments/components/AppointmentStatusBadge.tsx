import { Badge } from "@/shared/components/ui/badge";
import { cn } from "@/shared/utils/cn";
import { AppointmentStatus } from "../appointment.types";

interface AppointmentStatusBadgeProps {
  status: AppointmentStatus;
}

const STATUS_STYLES: Record<AppointmentStatus, string> = {
  [AppointmentStatus.Pending]:   "border-yellow-300 bg-yellow-100 text-yellow-800 hover:bg-yellow-100",
  [AppointmentStatus.Confirmed]: "border-green-300 bg-green-100 text-green-800 hover:bg-green-100",
  [AppointmentStatus.Cancelled]: "border-red-300 bg-red-100 text-red-800 hover:bg-red-100",
  [AppointmentStatus.Completed]: "border-blue-300 bg-blue-100 text-blue-800 hover:bg-blue-100",
  [AppointmentStatus.NoShow]:    "border-gray-300 bg-gray-100 text-gray-600 hover:bg-gray-100",
};

const STATUS_LABELS: Record<AppointmentStatus, string> = {
  // "Requested" instead of "Pending" — avoids reading as a payment state
  // (the payments UI uses "Pending" for unpaid card intents)
  [AppointmentStatus.Pending]:   "Requested",
  [AppointmentStatus.Confirmed]: "Confirmed",
  [AppointmentStatus.Cancelled]: "Cancelled",
  [AppointmentStatus.Completed]: "Completed",
  [AppointmentStatus.NoShow]:    "No Show",
};

export function AppointmentStatusBadge({ status }: AppointmentStatusBadgeProps) {
  return (
    <Badge
      variant="outline"
      className={cn(STATUS_STYLES[status] ?? "border-gray-300 bg-gray-100 text-gray-600")}
    >
      {STATUS_LABELS[status] ?? status}
    </Badge>
  );
}
