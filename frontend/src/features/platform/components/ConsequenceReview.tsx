import type { ReactNode } from "react";
import { Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";

export interface ConsequenceReviewProps {
  /** Unique per page and row, so several reviews on one page keep distinct element ids. */
  id:               string;
  /** What the action is: "Cancel subscription", "Extend trial", "Activate (cash payment)". */
  title:            string;
  /** Who it happens to. */
  studioName:       string;
  /** Inputs that change the consequence (days, plan, refund rule). Rendered above the consequence. */
  children?:        ReactNode;
  /** What will happen, in order. A single entry may be a "working it out" or "choose X first" line. */
  happens:          ReactNode[];
  /** What cannot be undone. Always stated; an action with nothing permanent says how it is undone instead. */
  permanent:        ReactNode[];
  /** A failure or a changed-consequence notice, shown above the buttons. */
  alert?:           ReactNode;
  confirmLabel:     string;
  destructive?:     boolean;
  busy:             boolean;
  confirmDisabled?: boolean;
  onConfirm:        () => void;
  onBack:           () => void;
}

/**
 * The one shape every admin subscription action takes before it is confirmed (slice 8 phase 3, D03): what will
 * happen, to whom, and what cannot be undone, with the inputs that change it above and the confirm below. Every entry
 * point (studio list row, studio detail, subscription oversight, dashboard at-risk row) renders this rather than its
 * own wording, so an action reads the same wherever an admin meets it.
 */
export function ConsequenceReview({
  id, title, studioName, children, happens, permanent, alert, confirmLabel, destructive = false, busy,
  confirmDisabled = false, onConfirm, onBack,
}: ConsequenceReviewProps) {
  const titleId = `${id}-title`;
  return (
    <section aria-labelledby={titleId} className="pt-2 space-y-2 border-t">
      <div>
        <h3 id={titleId} className={`text-xs font-medium ${destructive ? "text-destructive-text" : ""}`}>{title}</h3>
        <p className="text-xs text-muted-foreground">For {studioName}</p>
      </div>

      {children}

      <div className="space-y-1">
        <p className="text-[11px] font-medium uppercase text-muted-foreground" id={`${id}-happens`}>What happens</p>
        <ul aria-labelledby={`${id}-happens`} className="list-disc pl-4 space-y-0.5 text-xs">
          {happens.map((line, i) => <li key={i}>{line}</li>)}
        </ul>
      </div>

      <div className="space-y-1">
        <p className="text-[11px] font-medium uppercase text-muted-foreground" id={`${id}-permanent`}>Can&apos;t be undone</p>
        <ul aria-labelledby={`${id}-permanent`} className="list-disc pl-4 space-y-0.5 text-xs">
          {permanent.map((line, i) => <li key={i}>{line}</li>)}
        </ul>
      </div>

      {alert}

      <div className="flex gap-2">
        <Button
          size="sm"
          variant={destructive ? "destructive" : "default"}
          className="h-7 px-2 text-xs"
          disabled={busy || confirmDisabled}
          onClick={onConfirm}
        >
          {busy ? <Loader2 className="h-3 w-3 animate-spin" aria-label="Working" /> : confirmLabel}
        </Button>
        <Button size="sm" variant="ghost" className="h-7 px-2 text-xs" onClick={onBack}>Back</Button>
      </div>
    </section>
  );
}

/** A failure inside a review, focused when it appears. Callers key it by attempt so each new failure takes focus again. */
export function ReviewAlert({ children }: { children: ReactNode }) {
  return (
    <div role="alert" tabIndex={-1} ref={(el) => el?.focus()}
      className="rounded-md border border-destructive/40 px-2 py-1.5 text-xs text-destructive-text space-y-0.5">
      {children}
    </div>
  );
}
