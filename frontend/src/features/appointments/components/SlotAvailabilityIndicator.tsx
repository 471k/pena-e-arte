import { AlertCircle, CheckCircle2, Loader2 } from "lucide-react";
import type { SlotAvailabilityResponse } from "../appointment.types";

export function SlotAvailabilityIndicator({
  checking,
  status,
}: {
  checking: boolean;
  status:   SlotAvailabilityResponse | undefined;
}) {
  if (checking) {
    return (
      <p className="flex items-center gap-1.5 text-xs text-muted-foreground">
        <Loader2 className="h-3 w-3 animate-spin" aria-hidden="true" />
        Checking availability…
      </p>
    );
  }
  if (!status) return null;

  if (status.available) {
    return (
      <p className="flex items-center gap-1.5 text-xs text-green-600 dark:text-green-400">
        <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
        This slot is available
      </p>
    );
  }

  return (
    <p className="flex items-center gap-1.5 text-xs text-destructive-text" role="alert">
      <AlertCircle className="h-3.5 w-3.5" aria-hidden="true" />
      {status.reason ?? "This slot is not available."}
    </p>
  );
}
