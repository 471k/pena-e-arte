// Time off is stored as studio-local whole days (YYYY-MM-DD), so these format the date as written,
// never shifting it through the viewer's timezone.

/** "10 Nov 2026" for a studio-local YYYY-MM-DD date, without any timezone shift. */
export function formatLocalDay(ymd: string): string {
  const [y, m, d] = ymd.slice(0, 10).split("-").map(Number);
  return new Date(y, m - 1, d).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
}

export function formatLocalRange(startYmd: string, endYmd: string): string {
  return startYmd.slice(0, 10) === endYmd.slice(0, 10)
    ? formatLocalDay(startYmd)
    : `${formatLocalDay(startYmd)} – ${formatLocalDay(endYmd)}`;
}
