import { RotateCw } from "lucide-react";
import { Button } from "@/shared/components/ui/button";

/**
 * Why the appointment picker has nothing usable in it, with a way to retry: the appointments didn't
 * load, or the studio's timezone didn't (so their dates can't be shown in studio time, S01). Either is
 * a failure, never "you have no appointments".
 */
export function AppointmentPickerProblem({
  reason, onRetry,
}: { reason: "appointments" | "timezone"; onRetry: () => void }) {
  return (
    <div className="flex flex-wrap items-center gap-2" role="alert">
      <p className="text-xs text-destructive-text">
        {reason === "appointments"
          ? "Couldn't load your appointments."
          : "Couldn't load the studio's time zone, so appointment dates can't be shown."}
      </p>
      <Button type="button" variant="outline" size="sm" className="h-7 gap-1.5 text-xs" onClick={onRetry}>
        <RotateCw aria-hidden="true" className="h-3 w-3" />
        Try again
      </Button>
    </div>
  );
}
