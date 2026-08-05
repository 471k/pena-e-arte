import { useEffect, useState } from "react";
import type { LiveConnectionState } from "@/shared/hooks/useLiveTrafficHub";
import { formatRelativeTime } from "@/shared/utils/formatRelativeTime";

const STATE_COPY: Record<LiveConnectionState, { label: string; dot: string }> = {
  connected:    { label: "Live",          dot: "bg-emerald-500" },
  connecting:   { label: "Connecting…",   dot: "bg-muted-foreground" },
  reconnecting: { label: "Reconnecting…", dot: "bg-amber-500" },
  disconnected: { label: "Offline",       dot: "bg-red-500" },
};

// `label` deliberately keeps showing the previous tick's value across a `ts` change instead of
// resetting to null — a label that's stale by at most one 1s tick (still reading e.g. "3s ago"
// for a moment right after a fresh push) is imperceptible, whereas resetting to null made the
// "· Updated Xs ago" text visibly disappear and reappear on every single SignalR push. The only
// setState call site is inside the interval callback (a genuine external-timer subscription),
// never synchronously in the effect body.
function useRelativeSeconds(ts: number | null): string | null {
  const [label, setLabel] = useState<string | null>(null);

  useEffect(() => {
    if (ts === null) return;
    const id = setInterval(() => {
      setLabel(formatRelativeTime(Date.now() - ts));
    }, 1000);
    return () => clearInterval(id);
  }, [ts]);

  return ts === null ? null : label;
}

export function LiveStatusBadge({
  connectionState, lastUpdatedAt, isRefreshing, onRefresh,
}: {
  connectionState: LiveConnectionState;
  lastUpdatedAt: number | null;
  isRefreshing?: boolean;
  onRefresh?: () => void;
}) {
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
        <button
          type="button"
          onClick={onRefresh}
          disabled={isRefreshing}
          className={`ml-1 hover:text-foreground disabled:opacity-60 ${isRefreshing ? "animate-spin" : ""}`}
          aria-label="Refresh now"
        >
          ↻
        </button>
      )}
    </div>
  );
}
