import { createContext, useCallback, useContext, useState } from "react";
import { toast } from "sonner";

/**
 * A durable result for an admin action (D04): what happened and the resulting state, when (UTC), and the audit log
 * filtered to that action (and studio, when the action has one). It stays until dismissed; it is not a toast.
 * One implementation for every admin action, cash activation included.
 */
export interface ActionReceipt {
  /** One sentence naming the entity and the resulting state, e.g. "Ink Soul is suspended." */
  summary:   string;
  /** The audit action name the command writes (AuditActions in Pena_e_Arte.Domain), e.g. "Studio.Suspended". */
  action:    string;
  /** The studio the entry is filed under, when there is one; the link then filters to it too. */
  studioId?: string | null;
  /** ISO time the action was confirmed by the server. */
  at:        string;
}


/** The audit-log page reads `action` and `studioId` from its query string (AuditLogPage's FILTER_KEYS). */
export function auditLogHref(action: string, studioId?: string | null): string {
  const params = new URLSearchParams();
  if (studioId) params.set("studioId", studioId);
  params.set("action", action);
  return `/platform/audit-log?${params.toString()}`;
}


/** A receipt stamped now: call it right after the server confirmed the action. */
export function makeReceipt(summary: string, action: string, studioId?: string | null): ActionReceipt {
  return { summary, action, studioId: studioId ?? null, at: new Date().toISOString() };
}


/**
 * Local receipt state for one action site: `show` records a success, `put` takes one made elsewhere (a review
 * component, or a page that navigated here), `dismiss` clears it.
 */
export function useActionReceipt(initial: ActionReceipt | null = null): {
  receipt: ActionReceipt | null;
  show:    (summary: string, action: string, studioId?: string | null) => void;
  put:     (receipt: ActionReceipt) => void;
  dismiss: () => void;
} {
  const [receipt, setReceipt] = useState<ActionReceipt | null>(initial);
  const show = useCallback((summary: string, action: string, studioId?: string | null) => {
    setReceipt(makeReceipt(summary, action, studioId));
  }, []);
  const put = useCallback((r: ActionReceipt) => setReceipt(r), []);
  const dismiss = useCallback(() => setReceipt(null), []);
  return { receipt, show, put, dismiss };
}


/** A receipt carried in router state, for an action whose page navigates away on success. */
export function receiptFromState(state: unknown): ActionReceipt | null {
  if (!state || typeof state !== "object" || !("receipt" in state)) return null;
  const r = (state as { receipt: unknown }).receipt;
  if (!r || typeof r !== "object") return null;
  const c = r as Partial<ActionReceipt>;
  return typeof c.summary === "string" && typeof c.action === "string" && typeof c.at === "string"
    ? { summary: c.summary, action: c.action, at: c.at, studioId: typeof c.studioId === "string" ? c.studioId : null }
    : null;
}


/** The action names the admin screens link to: the same strings the commands write (AuditActions.cs). */
export const AUDIT_ACTIONS = {
  studioSuspended:          "Studio.Suspended",
  studioUnsuspended:        "Studio.Unsuspended",
  trialExtended:            "Studio.TrialExtended",
  subscriptionCancelled:    "Subscription.CancelledByAdmin",
  subscriptionActivated:    "Subscription.ActivatedManually",
  dunningExclusionChanged:  "Subscription.DunningExclusionChanged",
  billedAmountsBackfilled:  "Platform.BilledAmountsBackfilled",
  revenueLedgerBackfilled:  "Platform.RevenueLedgerBackfilled",
  planCreated:              "Plan.Created",
  planUpdated:              "Plan.Updated",
  planDeleted:              "Plan.Deleted",
  referralCodeGenerated:    "ReferralCode.GeneratedByAdmin",
  referralCodeDeactivated:  "ReferralCode.Deactivated",
  referralCodeReactivated:  "ReferralCode.Reactivated",
  referralCodeDeleted:      "ReferralCode.Deleted",
  industryReportTriggered:  "Platform.IndustryReportTriggered",
  supportSessionStarted:    "ImpersonationSession.Started",
  supportSessionEnded:      "ImpersonationSession.Ended",
  feedbackStatusUpdated:    "Feedback.StatusUpdated",
  conductReportStatusUpdated: "ConductReport.StatusUpdated",
} as const;


/** The nearest ActionReceiptArea's setter; null outside one. Read through useReportReceipt. */
export const ReportReceipt = createContext<((receipt: ActionReceipt) => void) | null>(null);

/** Report a receipt to the nearest ActionReceiptArea. Outside one, it falls back to a toast rather than vanishing. */
export function useReportReceipt(): (receipt: ActionReceipt) => void {
  const put = useContext(ReportReceipt);
  return useCallback((receipt: ActionReceipt) => {
    if (put) put(receipt);
    else toast.success(receipt.summary);
  }, [put]);
}

