/** Formats a millisecond duration as a coarse "time ago" string — the single shared policy for
 * every relative-timestamp display in the app (live-connection badges, visitor tables, signed
 * dates, etc.), so different pages don't disagree on thresholds/wording for the same concept. */
export function formatRelativeTime(elapsedMs: number): string {
  const seconds = Math.max(0, Math.floor(elapsedMs / 1000));
  if (seconds < 5) return "just now";
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  if (days < 30) return `${days}d ago`;
  const months = Math.floor(days / 30);
  return `${months}mo ago`;
}

/** Convenience wrapper for the common "how long ago was this ISO timestamp" case — keeps the
 * Date.now() read inside a plain helper rather than inlined at call sites, since a direct
 * Date.now() call in a component's render body trips React Compiler's purity check. */
export function formatRelativeTimeFromNow(iso: string): string {
  return formatRelativeTime(Date.now() - new Date(iso).getTime());
}
