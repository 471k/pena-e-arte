import { formatStudioTime, type StudioZone } from "@/shared/utils/formatInStudioTimezone";
import type { WaitlistEntryResponse } from "./waitlist.types";

const DATE_OPTIONS: Intl.DateTimeFormatOptions = { day: "numeric", month: "short", year: "numeric" };

/** The entry's own zone as a state. It travels on the entry, so it is never "still loading". */
export function waitlistZone(entry: Pick<WaitlistEntryResponse, "studioTimezone">): StudioZone {
  return entry.studioTimezone ? { status: "ready", timeZone: entry.studioTimezone } : { status: "error" };
}

/**
 * A day on a waitlist entry (the preferred range, an offered slot) as the STUDIO's calendar names it (S01).
 * A client elsewhere was told the wrong day for an offered slot, and an offer expires, so a wrong day can
 * cost them the slot. A missing zone reads "Time unavailable" rather than falling back to the viewer's.
 */
export function formatWaitlistDate(iso: string, entry: Pick<WaitlistEntryResponse, "studioTimezone">): string {
  return formatStudioTime(iso, DATE_OPTIONS, waitlistZone(entry));
}
