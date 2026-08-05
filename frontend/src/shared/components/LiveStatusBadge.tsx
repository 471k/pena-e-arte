import { useEffect, useState } from "react";
import type { LiveConnectionState } from "@/shared/hooks/useLiveTrafficHub";

const STATE_COPY: Record<LiveConnectionState, { label: string; dot: string }> = {
  connected:    { label: "Live",          dot: "bg-emerald-500" },
  connecting:   { label: "Connecting…",   dot: "bg-muted-foreground" },
  reconnecting: { label: "Reconnecting…", dot: "bg-amber-500" },
  disconnected: { label: "Offline",       dot: "bg-red-500" },
};

// Bundling `ts` into the same state value as `label` (rather than a ref) means a stale label
// from a previous ts never flashes before the next interval tick recomputes it, without reading
// a ref during render. The only setState call site is inside the interval callback (a genuine
// external-timer subscription), never synchronously in the effect body.
function useRelativeSeconds(ts: number | null): string | null {
  const [computed, setComputed] = useState<{ forTs: number; label: string } | null>(null);

  useEffect(() => {
    if (ts === null) return;
    const id = setInterval(() => {
      const seconds = Math.max(0, Math.floor((Date.now() - ts) / 1000));
      setComputed({ forTs: ts, label: seconds < 5 ? "just now" : `${seconds}s ago` });
    }, 1000);
    return () => clearInterval(id);
  }, [ts]);

  return ts !== null && computed?.forTs === ts ? computed.label : null;
}

export function LiveStatusBadge({
  connectionState, lastUpdatedAt, onRefresh,
}: { connectionState: LiveConnectionState; lastUpdatedAt: number | null; onRefresh?: () => void }) {
  const copy = STATE_COPY[connectionState];
  const relative = useRelativeSeconds(lastUpdatedAt);

  return (
    <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
      <span
        className={`h-1.5 w-1.5 rounded-full ${copy.dot} ${connectionState === "connected" ? "animate-pulse" : ""}`}
        aria-hidden="true"
      />
      <span>{copy.label}</span>
      {relative && <span>· Updated {relative}</span>}
      {onRefresh && (
        <button type="button" onClick={onRefresh} className="ml-1 hover:text-foreground" aria-label="Refresh now">
          ↻
        </button>
      )}
    </div>
  );
}
