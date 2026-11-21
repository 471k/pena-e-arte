import { AlertCircle, CheckCircle2, Loader2 } from "lucide-react";
import { LiveAnnouncement } from "@/shared/components/LiveAnnouncement";
import type { SlotAvailabilityResponse } from "../appointment.types";

/**
 * The live check for a typed date and time. Render it unconditionally: it holds the live region, which must
 * already exist when its text changes (S06). With `checking` false and no `status` it renders only that empty
 * region.
 *
 * Announcement rule (LiveAnnouncement): the person started this check by choosing a time, so its RESULT is
 * announced once, politely, whether available or not. "Not available" used to be role="alert", which
 * interrupts; it is an answer, not a failure. "Checking…" is transient and is not announced. Before this,
 * neither it nor "available" was announced and only the refusal was, at the loudest level.
 */
export function SlotAvailabilityIndicator({
  checking,
  status,
}: {
  checking: boolean;
  status:   SlotAvailabilityResponse | undefined;
}) {
  const settled = !checking && status !== undefined;

  return (
    <>
      {checking && (
        <p className="flex items-center gap-1.5 text-xs text-muted-foreground">
          <Loader2 className="h-3 w-3 animate-spin" aria-hidden="true" />
          Checking availability…
        </p>
      )}
      {/* Emptied while a check runs, so the same answer twice is read twice. */}
      <LiveAnnouncement className={settled ? undefined : "sr-only"}>
        {settled && status.available && (
          <p className="flex items-center gap-1.5 text-xs text-success-text dark:text-green-400">
            <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
            This slot is available
          </p>
        )}
        {settled && !status.available && (
          <p className="flex items-center gap-1.5 text-xs text-destructive-text">
            <AlertCircle className="h-3.5 w-3.5" aria-hidden="true" />
            {status.reason ?? "This slot is not available."}
          </p>
        )}
      </LiveAnnouncement>
    </>
  );
}
