import { History, RotateCw } from "lucide-react";
import { Button } from "@/shared/components/ui/button";

export type DraftNoticeState =
  | { kind: "loading" }
  | { kind: "error"; onRetry: () => void }
  | { kind: "offer"; savedAtLabel: string; onResume: () => void; onDiscard: () => void; busy: boolean };

/**
 * The restore affordance for a saved booking draft (S04). It offers the draft before anything is overwritten and
 * says in one sentence which answer was not kept, and why.
 */
export function DraftRestoreNotice({ state }: { state: DraftNoticeState }) {
  if (state.kind === "loading") {
    return <p role="status" className="text-xs text-muted-foreground">Checking for a booking you started earlier…</p>;
  }
  if (state.kind === "error") {
    return (
      <div role="alert" className="flex flex-wrap items-center gap-2 rounded-md border border-dashed px-3 py-2 text-xs">
        <span>Couldn&apos;t check for a booking you started earlier. You can still book from scratch.</span>
        <Button type="button" size="sm" variant="outline" className="h-7 gap-1 text-xs" onClick={state.onRetry}>
          <RotateCw className="h-3 w-3" aria-hidden="true" /> Try again
        </Button>
      </div>
    );
  }
  return (
    <section aria-label="Unfinished booking request" className="space-y-2 rounded-md border bg-muted/30 px-3 py-2.5 text-sm">
      <p className="flex items-start gap-2 font-medium">
        <History className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
        You started a booking request here on {state.savedAtLabel}.
      </p>
      <p className="text-xs text-muted-foreground">
        Everything you entered was kept except your answer to &quot;Anything else I should know?&quot;, because it can
        hold health details. You&apos;d add it again.
      </p>
      <div className="flex flex-wrap gap-2">
        <Button type="button" size="sm" className="h-8 text-xs" onClick={state.onResume} disabled={state.busy}>Resume it</Button>
        <Button type="button" size="sm" variant="outline" className="h-8 text-xs" onClick={state.onDiscard} disabled={state.busy}>
          Start over
        </Button>
      </div>
    </section>
  );
}
