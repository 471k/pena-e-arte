/**
 * Formats a Date as the value a `<input type="datetime-local">` expects — local wall-clock
 * time (`YYYY-MM-DDTHH:mm`), not UTC. `date.toISOString().slice(0, 16)` is a common but wrong
 * substitute here: it produces UTC, so the `min` bound on a datetime-local input silently
 * shifts by the visitor's UTC offset — blocking legitimate near-term slots for anyone west of
 * UTC, or allowing already-past times for anyone east of it. Found via /code-review, 2026-09-01.
 */
export function toLocalDatetimeInputValue(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
         `T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
