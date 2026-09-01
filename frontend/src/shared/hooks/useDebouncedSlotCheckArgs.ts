import { useEffect, useState } from "react";
import type { CheckSlotAvailabilityParams } from "@/features/appointments/appointment.types";

/**
 * Debounces the artist/date/duration inputs of a booking form into the args shape a
 * slot-availability query expects, returning null while the inputs aren't yet complete or the
 * 600ms debounce hasn't settled. Shared by BookAppointmentForm and GuestBookAppointmentForm,
 * which had identical copies of this effect differing only in which RTK Query hook consumed
 * the result. Found via /code-review, 2026-09-01.
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
      if (!date || !durationMinutes || (!bookAnyArtist && !artistId)) {
        setDebounced(null);
        return;
      }
      setDebounced({
        artistId: bookAnyArtist ? undefined : (artistId ?? undefined),
        date,
        durationMinutes,
      });
    }, delay);
    return () => clearTimeout(timer);
  }, [artistId, bookAnyArtist, date, durationMinutes]);

  return debounced;
}
