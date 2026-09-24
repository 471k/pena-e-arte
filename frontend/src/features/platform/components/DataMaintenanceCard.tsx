import { useState } from "react";
import { Link } from "react-router-dom";
import { CheckCircle2, DatabaseZap, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import {
  useBackfillBilledAmountsMutation,
  useBackfillRevenueLedgerMutation,
} from "@/features/platform/platformApi";
import type {
  BackfillBilledAmountsResponse,
  BackfillRevenueLedgerResponse,
} from "@/features/platform/platform.types";

function formatEuro(amount: number): string {
  return new Intl.NumberFormat("en-GB", { style: "currency", currency: "EUR" }).format(amount);
}

interface StepProps {
  step:        number;
  title:       string;
  description: string;
  running:     boolean;
  onRun:       () => void;
  children?:   React.ReactNode;
}

/** One backfill: a Run button that asks for an inline confirmation first (same pattern as the
 *  Cancel Subscription row action on this page), then shows whatever the caller renders as the
 *  result. Both backfills write to the database, hence the extra click. */
function BackfillStep({ step, title, description, running, onRun, children }: StepProps) {
  const [confirming, setConfirming] = useState(false);

  return (
    <li className="space-y-2">
      <div className="flex items-start justify-between gap-3 flex-wrap">
        <div className="min-w-0 flex-1">
          <p className="text-sm font-medium">
            <span className="text-muted-foreground">Step {step} · </span>
            {title}
          </p>
          <p className="text-xs text-muted-foreground mt-0.5">{description}</p>
        </div>

        {confirming ? (
          <div className="flex items-center gap-2" role="group" aria-label={`Confirm: ${title}`}>
            <span className="text-xs text-muted-foreground">This writes to the database. Run now?</span>
            <Button
              size="sm" className="h-7 text-xs"
              disabled={running}
              onClick={() => { setConfirming(false); onRun(); }}
            >
              Confirm
            </Button>
            <Button size="sm" variant="outline" className="h-7 text-xs" onClick={() => setConfirming(false)}>
              Cancel
            </Button>
          </div>
        ) : (
          <Button
            size="sm" variant="outline" className="h-7 text-xs gap-1"
            disabled={running}
            onClick={() => setConfirming(true)}
            aria-label={`Run: ${title}`}
          >
            {running && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            {running ? "Running…" : "Run"}
          </Button>
        )}
      </div>
      {children}
    </li>
  );
}

function ResultLine({ children }: { children: React.ReactNode }) {
  return (
    <p role="status" className="flex items-start gap-1.5 text-xs text-emerald-700 dark:text-emerald-400">
      <CheckCircle2 className="h-3.5 w-3.5 mt-0.5 shrink-0" aria-hidden="true" />
      <span>{children}</span>
    </p>
  );
}

/**
 * Admin-only "Data maintenance" card on the Subscription Oversight page: triggers the two
 * one-time, idempotent backfills that used to be curl/Postman-only. Order matters — the revenue
 * ledger copies each subscription's billed-amount snapshot into its first MRR event, so the
 * billed-amount snapshot runs first. See architecture.md Decisions Log, "Subscription billed-amount
 * snapshot (2026-09-23)" and "Subscription revenue ledger (2026-09-24)".
 */
export function DataMaintenanceCard() {
  const [runBilled,  { isLoading: billedRunning }]  = useBackfillBilledAmountsMutation();
  const [runLedger,  { isLoading: ledgerRunning }]  = useBackfillRevenueLedgerMutation();
  const [billedResult, setBilledResult] = useState<BackfillBilledAmountsResponse | null>(null);
  const [ledgerResult, setLedgerResult] = useState<BackfillRevenueLedgerResponse | null>(null);

  async function handleBilled() {
    try {
      setBilledResult(await runBilled().unwrap());
      toast.success("Billed-amount backfill finished");
    } catch {
      toast.error("Billed-amount backfill failed");
    }
  }

  async function handleLedger() {
    try {
      setLedgerResult(await runLedger().unwrap());
      toast.success("Revenue-ledger backfill finished");
    } catch {
      toast.error("Revenue-ledger backfill failed");
    }
  }

  return (
    <Card className="mt-6">
      <CardHeader className="pb-2">
        <CardTitle className="text-sm flex items-center gap-2">
          <DatabaseZap className="h-4 w-4" aria-hidden="true" />
          Data maintenance
        </CardTitle>
        <p className="text-xs text-muted-foreground">
          One-time backfills for subscriptions that existed before these features. Run them in order.
          Both are safe to run again — only data that is still missing is filled in.
        </p>
      </CardHeader>
      <CardContent>
        <ol className="space-y-5">
          <BackfillStep
            step={1}
            title="Billed-amount snapshot"
            description="Records what each existing subscription is actually billed (card-billed from Stripe, cash-billed from the plan's monthly price) so MRR stops using the live price list."
            running={billedRunning}
            onRun={handleBilled}
          >
            {billedResult && (
              <div className="space-y-1.5">
                <ResultLine>
                  {billedResult.cardBilledUpdated} card-billed updated
                  {billedResult.cardBilledSkipped > 0 && `, ${billedResult.cardBilledSkipped} skipped (not found in Stripe)`}
                  {`, ${billedResult.cashBilledSnapshots.length} cash-billed snapshotted`}.
                </ResultLine>
                {billedResult.cashBilledSnapshots.length > 0 && (
                  <div className="rounded-md border p-2">
                    <p className="text-xs text-muted-foreground mb-1">
                      Cash-billed studios were assumed to pay their plan's monthly price — please review:
                    </p>
                    <ul className="text-xs space-y-0.5 max-h-32 overflow-y-auto">
                      {[...billedResult.cashBilledSnapshots].sort((a, b) => b.price - a.price).map((c) => (
                        <li key={c.studioId} className="flex items-center justify-between gap-2">
                          <Link to={`/platform/studios/${c.studioId}`} className="underline underline-offset-2 truncate">
                            Studio {c.studioId.slice(0, 8)}
                          </Link>
                          <span className="tabular-nums">{formatEuro(c.price)}/mo</span>
                        </li>
                      ))}
                    </ul>
                  </div>
                )}
              </div>
            )}
          </BackfillStep>

          <BackfillStep
            step={2}
            title="Revenue ledger"
            description="Seeds recorded MRR history for every currently-active paying subscription, so the dashboard's MRR trend, movements chart and retention figures read real history instead of estimates."
            running={ledgerRunning}
            onRun={handleLedger}
          >
            {ledgerResult && (
              <div className="space-y-1.5">
                <ResultLine>
                  {ledgerResult.created} subscription{ledgerResult.created === 1 ? "" : "s"} seeded
                  {`, ${ledgerResult.skippedAlreadyInLedger} already in the ledger, ${ledgerResult.skippedNotBilling} not currently billing`}.
                </ResultLine>
                {ledgerResult.created === 0 && ledgerResult.skippedAlreadyInLedger > 0 && (
                  <p className="text-xs text-muted-foreground">Nothing new to seed — this has already been run.</p>
                )}
                <p className="text-xs text-muted-foreground">
                  Months before a subscription that has since cancelled can read lower than the old estimate,
                  because only subscriptions that are active now are seeded.
                </p>
              </div>
            )}
          </BackfillStep>
        </ol>
      </CardContent>
    </Card>
  );
}
