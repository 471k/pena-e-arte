import { cn } from "@/shared/utils/cn";
import { AppointmentStatus } from "../appointment.types";

interface AppointmentStatusBadgeProps {
  status: AppointmentStatus;
}

const STATUS_STYLES: Record<AppointmentStatus, string> = {
  [AppointmentStatus.Pending]:   "bg-yellow-100 text-yellow-800",
  [AppointmentStatus.Confirmed]: "bg-green-100 text-green-800",
  [AppointmentStatus.Cancelled]: "bg-red-100 text-red-800",
  [AppointmentStatus.Completed]: "bg-blue-100 text-blue-800",
  [AppointmentStatus.NoShow]:    "bg-gray-100 text-gray-600",
};

const STATUS_LABELS: Record<AppointmentStatus, string> = {
  [AppointmentStatus.Pending]:   "Pending",
  [AppointmentStatus.Confirmed]: "Confirmed",
  [AppointmentStatus.Cancelled]: "Cancelled",
  [AppointmentStatus.Completed]: "Completed",
  [AppointmentStatus.NoShow]:    "No Show",
};

export function AppointmentStatusBadge({ status }: AppointmentStatusBadgeProps) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium",
        STATUS_STYLES[status] ?? "bg-gray-100 text-gray-600"
      )}
    >
      {STATUS_LABELS[status] ?? status}
    </span>
  );
}
