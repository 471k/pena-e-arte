import type { ReactNode } from "react";

/**
 * The one way this app announces a status to a screen reader (slice 6a, S06). Always mounted, so the live
 * region exists before its text changes (a region that mounts together with its text is often not read);
 * the caller controls WHEN it speaks by controlling when `message` changes.
 *
 * The rule:
 * - A change the person caused (saved, sent, a check they started, a search they ran) is announced once,
 *   politely. A failed action they caused uses `politeness="assertive"` (role="alert").
 * - A change they did not cause, or anything that ticks (a countdown, a progress number), is NOT announced on
 *   every change: render the visible value `aria-hidden` and pass a message that only changes at thresholds,
 *   as the support-session banner does with `expiryAnnouncement` (D05).
 *
 * An empty message is silence. Clearing it while work is in flight and setting it when the work settles is what
 * makes a repeat of the same result ("This slot is available", twice) be read twice.
 */
export function LiveAnnouncement({
  message,
  children,
  className,
  politeness = "polite",
}: {
  /** Screen-reader-only text. Use this OR `children`. */
  message?: string;
  /** Visible content that is itself the announcement (e.g. a result line already on screen), so the words
   *  aren't in the page twice. Keep icons inside it `aria-hidden`. */
  children?: ReactNode;
  /** Only applies with `children`; the `message` form is always visually hidden. */
  className?: string;
  politeness?: "polite" | "assertive";
}) {
  return (
    <div
      className={children !== undefined ? className : "sr-only"}
      role={politeness === "assertive" ? "alert" : "status"}
      aria-live={politeness}
      aria-atomic="true"
    >
      {children !== undefined ? children : message}
    </div>
  );
}
