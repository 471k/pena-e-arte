import { toLocalDatetimeInputValue } from "@/shared/utils/localDatetimeInput";

/**
 * C09: how a waitlist claim travels from the claim page to the booking form. The claim page navigates to
 * `/book?studio=<slug>` with this object in router state: never in the URL, so the token stays out of
 * anything a server logs. The forms only prefill from it and pass the token along; they don't fork.
 */
export interface WaitlistClaimHandoff {
  token:           string;
  artistId:        string | null;
  /** ISO instant of the offered slot, or null to book another time (the slot was taken). */
  slotStart:       string | null;
  durationMinutes: number;
}

/** Reads the handoff out of router state, or null when the visit didn't come from a claim. */
export function readWaitlistClaimHandoff(state: unknown): WaitlistClaimHandoff | null {
  if (typeof state !== "object" || state === null || !("waitlistClaim" in state)) return null;
  const claim: unknown = (state as { waitlistClaim: unknown }).waitlistClaim;
  if (typeof claim !== "object" || claim === null) return null;
  const c = claim as Record<string, unknown>;
  if (typeof c.token !== "string" || c.token.length === 0) return null;
  return {
    token:           c.token,
    artistId:        typeof c.artistId === "string" ? c.artistId : null,
    slotStart:       typeof c.slotStart === "string" ? c.slotStart : null,
    durationMinutes: typeof c.durationMinutes === "number" ? c.durationMinutes : 60,
  };
}

/** The claim token from the page's URL fragment (`#t=<token>`). */
export function readClaimTokenFromHash(hash: string): string | null {
  const params = new URLSearchParams(hash.startsWith("#") ? hash.slice(1) : hash);
  const token = params.get("t");
  return token && token.length > 0 ? token : null;
}

/**
 * Prefill for the booking forms' `defaultValues`. Duration is prefilled only when it's one of the form's
 * presets (the form's own rule); otherwise the form keeps its default and the client picks. The time is
 * written in the browser's zone because the forms read the input back with `new Date(value)`, so the
 * offered instant is what gets submitted.
 */
export function claimHandoffDefaults(
  handoff: WaitlistClaimHandoff | null,
  validDurations: readonly number[],
): { artistId?: string; bookAnyArtist?: boolean; scheduledAt?: string; durationMinutes?: number } {
  if (!handoff) return {};
  return {
    artistId:        handoff.artistId ?? "",
    bookAnyArtist:   handoff.artistId === null,
    ...(handoff.slotStart ? { scheduledAt: toLocalDatetimeInputValue(new Date(handoff.slotStart)) } : {}),
    ...(validDurations.includes(handoff.durationMinutes) ? { durationMinutes: handoff.durationMinutes } : {}),
  };
}
