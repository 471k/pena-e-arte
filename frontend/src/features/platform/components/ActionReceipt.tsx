import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { CheckCircle2, X } from "lucide-react";
import { formatUtc } from "./subscriptionConsequences";
import { auditLogHref, ReportReceipt, useActionReceipt, type ActionReceipt } from "./receipts";

/** The receipt itself (D04). The types, helpers and hooks live in ./receipts. */
export function ActionReceiptNotice({ receipt, onDismiss }: { receipt: ActionReceipt; onDismiss: () => void }) {
  return (
    <div role="status" className="flex items-start gap-2 rounded-md border border-green-200 bg-green-50 dark:border-green-800 dark:bg-green-950/30 px-3 py-2 text-xs">
      <CheckCircle2 className="h-4 w-4 shrink-0 text-green-700 dark:text-green-400" aria-hidden="true" />
      <div className="flex-1 min-w-0 space-y-0.5">
        <p className="font-medium">{receipt.summary}</p>
        <p className="text-muted-foreground">
          {formatUtc(receipt.at)} · <Link to={auditLogHref(receipt.action, receipt.studioId)} className="underline">View in the audit log</Link>
        </p>
      </div>
      <button type="button" onClick={onDismiss} className="rounded p-0.5 hover:bg-muted" aria-label="Dismiss this receipt">
        <X className="h-3.5 w-3.5" aria-hidden="true" />
      </button>
    </div>
  );
}


/**
 * A page-level place for receipts, for actions taken from a row that can leave the list afterwards: an at-risk
 * studio that becomes Trialing, or a row that no longer matches the status filter. The receipt outlives the row.
 */
export function ActionReceiptArea({ children, initial = null }: { children: ReactNode; initial?: ActionReceipt | null }) {
  const { receipt, put, dismiss } = useActionReceipt(initial);
  return (
    <ReportReceipt.Provider value={put}>
      {receipt && <ActionReceiptNotice receipt={receipt} onDismiss={dismiss} />}
      {children}
    </ReportReceipt.Provider>
  );
}

