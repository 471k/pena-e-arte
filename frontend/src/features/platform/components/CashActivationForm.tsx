import { useState } from "react";
import { Link } from "react-router-dom";
import { CheckCircle2, Loader2, X } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { extractErrorMessage } from "@/shared/utils/extractErrorMessage";
import { useActivateSubscriptionManuallyMutation } from "@/features/platform/platformApi";
import type { PlanResponse } from "@/features/billing/billing.types";
import { CASH_ACTIVATABLE, type SubscriptionKnowledge } from "./subscriptionKnowledge";

export interface ActivationReceipt {
  studioId:   string;
  studioName: string;
  planName:   string;
  status:     string;
  at:         string;
}

function formatUtc(iso: string): string {
  return `${new Date(iso).toLocaleString("en-GB", {
    day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit", timeZone: "UTC",
  })} UTC`;
}

/**
 * A durable result for a successful activation (D04): what happened, to which studio, the resulting state, when,
 * and the audit log filtered to that studio and action. It stays until dismissed; it is not a toast.
 */
export function ActivationReceiptNotice({ receipt, onDismiss }: { receipt: ActivationReceipt; onDismiss: () => void }) {
  const auditLink = `/platform/audit-log?studioId=${encodeURIComponent(receipt.studioId)}&action=Subscription.ActivatedManually`;
  return (
    <div role="status" className="flex items-start gap-2 rounded-md border border-green-200 bg-green-50 dark:border-green-800 dark:bg-green-950/30 px-3 py-2 text-xs">
      <CheckCircle2 className="h-4 w-4 shrink-0 text-green-700 dark:text-green-400" aria-hidden="true" />
      <div className="flex-1 space-y-0.5">
        <p className="font-medium">
          Subscription activated for {receipt.studioName}: now {receipt.status} on {receipt.planName}.
        </p>
        <p className="text-muted-foreground">
          {formatUtc(receipt.at)} · <Link to={auditLink} className="underline">View in the audit log</Link>
        </p>
      </div>
      <button type="button" onClick={onDismiss} className="rounded p-0.5 hover:bg-muted" aria-label="Dismiss this receipt">
        <X className="h-3.5 w-3.5" aria-hidden="true" />
      </button>
    </div>
  );
}

interface CashActivationFormProps {
  studioId:         string;
  studioName:       string;
  plans:            PlanResponse[];
  /** Element ids stay unique per row: e.g. "detail" or the studio id. */
  idSuffix:         string;
  notePlaceholder?: string;
  heading?:         string;
  /** The subscription as the page now knows it, used to decide whether a retry is still meaningful. */
  knowledge:        SubscriptionKnowledge;
  /** True while the subscription is being re-read (the query's isFetching): old data must not decide a retry. */
  checking:         boolean;
  /** Re-read the studio's subscription. Called after a failure, before any retry is allowed. */
  onRecheck:        () => void;
  onClose:          () => void;
  onActivated:      (receipt: ActivationReceipt) => void;
}

/**
 * Manual cash activation (D04). A failure keeps the chosen plan and the note, shows the reason inline and moves
 * focus to it, then re-reads the subscription: if the studio is no longer activatable (the first attempt may
 * have gone through), the retry is withheld rather than sent a second time.
 */
export function CashActivationForm({
  studioId, studioName, plans, idSuffix, notePlaceholder = "e.g. Cash paid in person", heading = "Activate — Cash Payment",
  knowledge, checking, onRecheck, onClose, onActivated,
}: CashActivationFormProps) {
  const [planId, setPlanId]   = useState("");
  const [note, setNote]       = useState("");
  const [failure, setFailure] = useState<{ message: string; attempt: number } | null>(null);
  const [activate, { isLoading }] = useActivateSubscriptionManuallyMutation();

  const settled             = failure !== null && !checking;
  const rechecking          = failure !== null && (checking || knowledge.kind === "loading");
  const recheckFailed       = settled && knowledge.kind === "failed";
  const stillActivatable    = settled && knowledge.kind === "known" && CASH_ACTIVATABLE.has(knowledge.status);
  const noLongerActivatable = settled && knowledge.kind === "known" && !CASH_ACTIVATABLE.has(knowledge.status);

  async function submit() {
    if (!planId) return;
    try {
      const result = await activate({ studioId, planId, note: note || undefined }).unwrap();
      onActivated({
        studioId,
        studioName,
        planName: plans.find((p) => p.id === planId)?.name ?? "the chosen plan",
        status:   result.status,
        at:       new Date().toISOString(),
      });
      onClose();
    } catch (err) {
      setFailure((prev) => ({
        message: extractErrorMessage(err, "The subscription wasn't activated."),
        attempt: (prev?.attempt ?? 0) + 1,
      }));
      onRecheck();
    }
  }

  return (
    <div className="pt-2 space-y-2 border-t">
      <p className="text-xs font-medium text-muted-foreground">{heading}</p>
      <div className="space-y-1">
        <Label htmlFor={`plan-${idSuffix}`} className="text-xs">Plan</Label>
        <select
          id={`plan-${idSuffix}`}
          value={planId}
          onChange={(e) => setPlanId(e.target.value)}
          className="h-8 w-full rounded-md border border-input bg-background px-2 text-xs"
        >
          <option value="">Select a plan…</option>
          {plans.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
        </select>
      </div>
      <div className="space-y-1">
        <Label htmlFor={`note-${idSuffix}`} className="text-xs">Note (optional)</Label>
        <Input id={`note-${idSuffix}`} value={note} onChange={(e) => setNote(e.target.value)}
          placeholder={notePlaceholder} className="h-8 text-xs" />
      </div>

      {failure && (
        // Keyed by attempt so each new failure remounts the alert and takes focus again.
        <div key={failure.attempt} role="alert" tabIndex={-1} ref={(el) => el?.focus()}
          className="rounded-md border border-destructive/40 px-2 py-1.5 text-xs text-destructive-text space-y-0.5">
          <p className="font-medium">Activation failed: {failure.message}</p>
          <p>
            {rechecking && "Checking the studio's current status before you retry…"}
            {recheckFailed && "The current status couldn't be checked, so retrying is paused. Reload the page to check again."}
            {noLongerActivatable && knowledge.kind === "known"
              && `The studio's subscription is now ${knowledge.status}, so there is nothing to activate. The earlier attempt may have gone through: check the audit log.`}
            {stillActivatable && "Your plan and note are kept. The studio still needs activating, so you can try again."}
          </p>
        </div>
      )}

      <div className="flex gap-2">
        <Button size="sm" className="h-7 px-2 text-xs flex-1"
          disabled={isLoading || !planId || rechecking || recheckFailed || noLongerActivatable}
          onClick={() => void submit()}>
          {isLoading ? <Loader2 className="h-3 w-3 animate-spin" /> : failure ? "Try again" : "Activate subscription"}
        </Button>
        <Button size="sm" variant="ghost" className="h-7 px-2 text-xs" onClick={onClose}>Cancel</Button>
      </div>
    </div>
  );
}
