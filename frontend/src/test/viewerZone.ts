/**
 * Runs `fn` with the process's own zone set to `timeZone`, the zone a browser's Date and Intl default to, and
 * restores it afterwards (deleting TZ if it was unset, since assigning undefined would set the string "undefined").
 * Node re-reads TZ when it is assigned at runtime, so this is how a test pins a viewer west or east of UTC without
 * depending on the runner's zone. CI runs in UTC, where a date-only value printed in the viewer's zone looks right.
 */
export async function withViewerZone<T>(timeZone: string, fn: () => Promise<T> | T): Promise<T> {
  const saved = process.env.TZ;
  process.env.TZ = timeZone;
  try {
    return await fn();
  } finally {
    if (saved === undefined) delete process.env.TZ;
    else process.env.TZ = saved;
  }
}
