import type { AvailableSlotsResponse } from "./appointment.types";
import { addWallDays, studioWallDate, type WallDate } from "@/shared/utils/studioDay";

/**
 * Helpers for the slot list (C03). The list is a preview, not a reservation: nothing here decides whether a time can
 * be booked. The server's create and reschedule commands do, and a taken slot comes back as SLOT_ALREADY_BOOKED.
 */

/** Days one slot-list request covers; the server allows at most 14. */
export const SLOT_RANGE_DAYS = 7;

const pad2 = (n: number) => String(n).padStart(2, "0");

/** A wall date as the API's "YYYY-MM-DD". */
export function wallDateKey(date: WallDate): string {
  return `${date.year}-${pad2(date.month)}-${pad2(date.day)}`;
}

/** Parses "YYYY-MM-DD" back into a wall date. */
export function parseWallDateKey(key: string): WallDate {
  const [year, month, day] = key.split("-").map(Number);
  return { year, month, day };
}

/** The request range starting on `start`: SLOT_RANGE_DAYS studio days. */
export function slotRange(start: WallDate): { from: string; to: string } {
  return { from: wallDateKey(start), to: wallDateKey(addWallDays(start, SLOT_RANGE_DAYS - 1)) };
}

/** The range start whose window contains `instant`, counted in whole windows from `today` (never before today). */
export function rangeStartContaining(instant: Date, today: WallDate, timeZone: string): WallDate {
  const target = studioWallDate(instant, timeZone);
  const days = Math.round(
    (Date.UTC(target.year, target.month - 1, target.day) - Date.UTC(today.year, today.month - 1, today.day)) / 86_400_000,
  );
  return addWallDays(today, Math.max(0, Math.floor(days / SLOT_RANGE_DAYS) * SLOT_RANGE_DAYS));
}

/**
 * Up to `count` offered starts nearest to `lostIso` (the time that was just taken), soonest first among equals.
 * Never chooses one: the person picks.
 */
export function nearestStarts(data: AvailableSlotsResponse | undefined, lostIso: string, count = 3): string[] {
  if (!data) return [];
  const lost = new Date(lostIso).getTime();
  return data.days
    .flatMap((d) => d.starts)
    .filter((s) => new Date(s).getTime() !== lost)
    .sort((a, b) => {
      const da = Math.abs(new Date(a).getTime() - lost);
      const db = Math.abs(new Date(b).getTime() - lost);
      return da - db || new Date(a).getTime() - new Date(b).getTime();
    })
    .slice(0, count)
    .sort((a, b) => new Date(a).getTime() - new Date(b).getTime());
}

/** True when a create or reschedule failed because someone else booked the time first (409 SLOT_ALREADY_BOOKED). */
export function isSlotTaken(error: unknown): boolean {
  if (typeof error !== "object" || error === null) return false;
  const { status, data } = error as { status?: unknown; data?: unknown };
  return status === 409
    && typeof data === "object" && data !== null
    && (data as { code?: unknown }).code === "SLOT_ALREADY_BOOKED";
}
