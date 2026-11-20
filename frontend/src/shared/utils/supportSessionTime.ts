/** Time helpers for the support-session banner (D05). */

/** Milliseconds from now until `expiresAt` (negative once it has passed). */
export function msUntil(expiresAt: string, now: number = Date.now()): number {
  return new Date(expiresAt).getTime() - now;
}

/** The visible countdown, "12:05 left". */
export function formatRemaining(ms: number): string {
  if (ms <= 0) return "expiring now";
  const minutes = Math.floor(ms / 60000);
  const seconds = Math.floor((ms % 60000) / 1000);
  return `${minutes}:${seconds.toString().padStart(2, "0")} left`;
}

/**
 * What a screen reader hears about the time left: only at these thresholds, never every tick (D05: "quiet").
 * The visible countdown is in a plain element; this text sits in a polite live region and changes only when
 * a threshold is crossed, so it is announced at most four times in a session.
 */
export function expiryAnnouncement(ms: number): string {
  if (ms <= 0) return "The support session is expiring now.";
  if (ms <= 60_000) return "1 minute left in this support session.";
  if (ms <= 5 * 60_000) return "5 minutes left in this support session.";
  if (ms <= 10 * 60_000) return "10 minutes left in this support session.";
  return "";
}
