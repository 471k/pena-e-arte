import { useEffect, useReducer } from "react";
import { X, Eye, LogOut, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { endImpersonation } from "@/features/auth/authSlice";
import { clearImpersonationScopeError } from "@/features/ui/uiSlice";
import { useEndImpersonationSessionMutation } from "@/features/platform/platformApi";
import { expiryAnnouncement, formatRemaining, msUntil } from "@/shared/utils/supportSessionTime";

/// Persistent, unmissable "Viewing as {studio}" banner for an active Support Impersonation
/// session — deliberately NOT dismissible (no close button), present at the layout root
/// (mounted in AppRoot) so it survives across every page while the session is active. See
/// docs/claude/architecture.md Decisions Log — "Support Impersonation with Audit Trail".
/// D05 (slice 8 phase 4): says it is read-only, shows the reason given for the session, and keeps the
/// countdown out of the accessibility tree's live announcements except at thresholds.
export function ImpersonationBanner() {
  const dispatch = useAppDispatch();
  const impersonation = useAppSelector((s) => s.auth.impersonation);
  const scopeError = useAppSelector((s) => s.ui.impersonationScopeError);
  const [endSession, { isLoading: ending }] = useEndImpersonationSessionMutation();
  // A tick counter, not the formatted string itself — "remaining" is always recomputed
  // fresh in the render body below, so there's no risk of it going stale between ticks;
  // the effect's only job is forcing a re-render once a second.
  const [, forceTick] = useReducer((n: number) => n + 1, 0);

  useEffect(() => {
    if (!impersonation) return;
    const interval = setInterval(forceTick, 1000);
    return () => clearInterval(interval);
  }, [impersonation]);

  if (!impersonation) return null;
  const msLeft = msUntil(impersonation.expiresAt);

  async function handleEndSession() {
    try {
      await endSession(impersonation!.sessionId).unwrap();
    } catch {
      // Even if the API call fails (e.g. the session already expired server-side), the
      // admin must still get their own session back client-side — never strand them on
      // an impersonation token that no longer works.
    }
    dispatch(endImpersonation());
    toast.success("Impersonation session ended.");
  }

  return (
    <div role="region" aria-label="Support session">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5 px-4 py-2.5 bg-indigo-600 text-white text-sm">
        <Eye className="h-4 w-4 shrink-0" aria-hidden="true" />
        <span className="font-medium">Viewing as {impersonation.studioName}</span>
        <span className="rounded bg-white/15 px-1.5 py-0.5 text-xs font-medium">Read-only</span>
        <span className="text-white/90 text-xs">
          Reason: {impersonation.reason ?? "not available in this tab"}
        </span>
        {/* Visible countdown, deliberately NOT live: it changes every second. */}
        <span className="text-white/80 text-xs tabular-nums" aria-hidden="true">{formatRemaining(msLeft)}</span>
        <span className="sr-only" aria-live="polite" aria-atomic="true">{expiryAnnouncement(msLeft)}</span>
        <span className="flex-1" />
        <button
          type="button"
          onClick={handleEndSession}
          disabled={ending}
          className="flex items-center gap-1.5 px-2.5 py-1 rounded-md bg-white/15 hover:bg-white/25 transition-colors font-medium disabled:opacity-60"
        >
          {ending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <LogOut className="h-3.5 w-3.5" />}
          End session
        </button>
      </div>

      {scopeError && (
        <div role="status" className="flex items-center gap-3 px-4 py-2 bg-amber-500/10 border-b border-amber-500/30 text-warning-text dark:text-amber-400 text-sm">
          <span className="flex-1">{scopeError}</span>
          <button
            type="button"
            aria-label="Dismiss"
            onClick={() => dispatch(clearImpersonationScopeError())}
            className="p-0.5 rounded hover:bg-amber-500/20 transition-colors"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
      )}
    </div>
  );
}
