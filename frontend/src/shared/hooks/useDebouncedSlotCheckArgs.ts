import { useEffect, useState } from "react";
import type { CheckSlotAvailabilityParams } from "@/features/appointments/appointment.types";

/**
 * Debounces the artist/date/duration inputs of a booking form into the args shape a
 * slot-availability query expects, returning null while the inputs aren't yet complete or the
 * 600ms debounce hasn't settled. Shared by BookAppointmentForm and GuestBookAppointmentForm,
 * which had identical copies of this effect differing only in which RTK Query hook consumed
 * the result. Found via /code-review, 2026-09-01.
 *
 * `date` is the form's datetime-local value; it's sent as a UTC instant (toISOString), exactly
 * like the booking submit, so the backend can compare it on the studio's own clock and against
 * stored (UTC) appointments. Sending the raw local value made the pre-check disagree with the
 * submit by the UTC offset (G-1, 2026-10-15). An incomplete/invalid date is not sent.
 */
export function useDebouncedSlotCheckArgs(
  artistId: string | null | undefined,
  bookAnyArtist: boolean,
  date: string | undefined,
  durationMinutes: number | undefined,
): CheckSlotAvailabilityParams | null {
  const [debounced, setDebounced] = useState<CheckSlotAvailabilityParams | null>(null);

  useEffect(() => {
    const ready = date && durationMinutes && (bookAnyArtist || artistId);
    const delay = ready ? 600 : 0;
    const timer = setTimeout(() => {
      const instant = date ? new Date(date) : null;
      if (!instant || Number.isNaN(instant.getTime()) || !durationMinutes || (!bookAnyArtist && !artistId)) {
        setDebounced(null);
        return;
      }
      setDebounced({
        artistId: bookAnyArtist ? undefined : (artistId ?? undefined),
        date: instant.toISOString(),
        durationMinutes,
      });
    }, delay);
    return () => clearTimeout(timer);
  }, [artistId, bookAnyArtist, date, durationMinutes]);

  return debounced;
}
