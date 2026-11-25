import { formatStudioTime } from "./formatInStudioTimezone";
import { studioZoneFrom } from "./studioDay";

const VISIT_DATE: Intl.DateTimeFormatOptions = { day: "numeric", month: "short", year: "numeric" };

/**
 * The day of a past visit, as the studio's calendar names it (S01), for the "which visit" pickers on the review
 * and conduct-report forms. An appointment locates something in the studio's operating day, so it follows the
 * studio's zone, not the reader's: a client in another zone was shown the wrong day and could pick the wrong
 * visit. The zone travels on the row, so a missing one reads "Time unavailable" and never falls back.
 */
export function formatVisitDate(visit: { date: string; studioTimezone: string | null }): string {
  return formatStudioTime(visit.date, VISIT_DATE, studioZoneFrom(visit.studioTimezone, true));
}
