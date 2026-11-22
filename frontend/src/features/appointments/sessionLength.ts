/**
 * How a session's length is said to a client, in one place (C04). The booking forms show it as a CONSEQUENCE of
 * the service rather than asking it as a question, and slice 7 phase C's review block must state the same number
 * in the same words, so both read from here.
 */
export function formatSessionLength(minutes: number): string {
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  if (h === 0) return `${m} min`;
  const hours = h === 1 ? "1 hour" : `${h} hours`;
  return m === 0 ? hours : `${hours} ${m} min`;
}

/** "2 hours, set by “Large colour session”" when a service decides it; "2 hours, the length you chose" otherwise. */
export function describeSessionLength(minutes: number, serviceName: string | null): string {
  const length = formatSessionLength(minutes);
  return serviceName ? `${length}, set by “${serviceName}”` : `${length}, the length you chose`;
}
