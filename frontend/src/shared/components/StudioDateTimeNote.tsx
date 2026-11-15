import { RotateCw } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { formatStudioTime, type StudioZone } from "@/shared/utils/formatInStudioTimezone";
import { resolveStudioWallTime, studioWallInstant } from "@/shared/utils/studioDay";

const TIME_WITH_ZONE: Intl.DateTimeFormatOptions = { hour: "2-digit", minute: "2-digit", timeZoneName: "short" };

function viewerTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone;
}

interface StudioDateTimeNoteProps {
  /** Referenced by the input's `aria-describedby`. */
  id:    string;
  zone:  StudioZone;
  /** The input's `datetime-local` value, read on the studio's wall clock. */
  value: string;
  /** Shown with the "couldn't load" line. Leave it out when the caller shows that state itself. */
  onRetry?: () => void;
  /** Whether to render the zone-error line here (RescheduleDialog shows it in place of its input). */
  showZoneError?: boolean;
}

/**
 * What a studio wall-clock date/time input means (S01), said the same way everywhere a time is picked:
 * the zone while it loads or if it can't be read, a time the clocks skip, a time that happens twice (the
 * first is used), and the viewer's own time when they are somewhere else. Pair it with a label saying
 * "(studio time, <zone>)" and an input whose `min` comes from `toStudioInputValue`.
 */
export function StudioDateTimeNote({ id, zone, value, onRetry, showZoneError = true }: StudioDateTimeNoteProps) {
  const timeZone = zone.status === "ready" ? zone.timeZone : null;
  const wall = timeZone && value ? resolveStudioWallTime(value, timeZone) : null;
  const picked = studioWallInstant(value, timeZone);

  return (
    <div id={id} aria-live="polite">
      {zone.status === "loading" && (
        <p className="text-xs text-muted-foreground">Loading the studio&apos;s time zone…</p>
      )}
      {zone.status === "error" && showZoneError && (
        <div className="flex flex-wrap items-center gap-2" role="alert">
          <p className="text-xs text-destructive-text">
            Couldn&apos;t load the studio&apos;s time zone, so a time can&apos;t be picked safely.
          </p>
          {onRetry && (
            <Button type="button" variant="outline" size="sm" className="gap-1.5" onClick={onRetry}>
              <RotateCw className="h-3.5 w-3.5" aria-hidden="true" />
              Try again
            </Button>
          )}
        </div>
      )}
      {wall?.kind === "gap" && (
        <p className="text-xs text-destructive-text" role="alert">
          That time doesn&apos;t exist on that day: the clocks go forward. Pick another time.
        </p>
      )}
      {wall?.kind === "repeated" && (
        <p className="text-xs text-warning-text">
          That time happens twice on that day because the clocks go back. The first one is used
          ({formatStudioTime(wall.first, TIME_WITH_ZONE, zone)}).
        </p>
      )}
      {picked && timeZone && viewerTimeZone() !== timeZone && (
        <p className="text-xs text-muted-foreground">
          That&apos;s {picked.toLocaleString("en-GB", {
            weekday: "short", day: "numeric", month: "short", hour: "2-digit", minute: "2-digit",
          })} your time.
        </p>
      )}
    </div>
  );
}
