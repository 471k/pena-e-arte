import { useState } from "react";
import { useSearchParams } from "react-router-dom";
import { Banknote, Loader2, Receipt } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import {
  useGetPlatformSubscriptionsQuery,
  useExtendTrialMutation,
  useActivateSubscriptionManuallyMutation,
  useCancelSubscriptionMutation,
} from "@/features/platform/platformApi";
import { useGetIssuerPlansQuery } from "@/features/billing/billingApi";
import type { PlatformSubscriptionResponse } from "@/features/platform/platform.types";

const STATUS_CLASSES: Record<string, string> = {
  Active:         "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300",
  Trialing:       "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300",
  PastDue:        "bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-300",
  GracePeriod:    "bg-orange-100 text-orange-700 dark:bg-orange-900/30 dark:text-orange-300",
  Cancelled:      "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300",
  NoSubscription: "bg-muted text-muted-foreground",
};

interface SubscriptionRowProps {
  sub: PlatformSubscriptionResponse;
}

const CASH_ACTIVATABLE = new Set(["NoSubscription", "PastDue", "GracePeriod", "Cancelled"]);
const CANCELLABLE      = new Set(["Active", "PastDue", "Trialing", "GracePeriod"]);

function SubscriptionRow({ sub }: SubscriptionRowProps) {
  const [extending,   setExtending]   = useState(false);
  const [activating,  setActivating]  = useState(false);
  const [confirming,  setConfirming]  = useState(false);
  const [days,        setDays]        = useState("7");
  const [cashPlanId,  setCashPlanId]  = useState("");
  const [cashNote,    setCashNote]    = useState("");

  const [extendTrial,      { isLoading: extending_ }]  = useExtendTrialMutation();
  const [activateManually, { isLoading: activating_ }] = useActivateSubscriptionManuallyMutation();
  const [cancelSub,        { isLoading: cancelling_ }] = useCancelSubscriptionMutation();
  const { data: plans = [] } = useGetIssuerPlansQuery();

  async function handleExtend() {
    const additionalDays = parseInt(days, 10);
    if (isNaN(additionalDays) || additionalDays < 1) return;
    await extendTrial({ studioId: sub.studioId, additionalDays }).unwrap();
    setExtending(false);
  }

  async function handleActivate() {
    if (!cashPlanId) return;
    await activateManually({
      studioId: sub.studioId,
      planId:   cashPlanId,
      note:     cashNote || undefined,
    }).unwrap();
    setActivating(false);
    setCashNote("");
    setCashPlanId("");
  }

  const statusClass = STATUS_CLASSES[sub.status] ?? STATUS_CLASSES.NoSubscription;
  const canActivate = CASH_ACTIVATABLE.has(sub.status);
  const canCancel   = CANCELLABLE.has(sub.status);

  async function handleCancel() {
    await cancelSub(sub.studioId).unwrap();
    setConfirming(false);
  }

  return (
    <Card>
      <CardContent className="p-4 space-y-2">
        <div className="flex items-start justify-between gap-4">
          <div className="space-y-0.5 min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <span className="font-medium text-sm">{sub.studioName}</span>
              <span className="text-xs text-muted-foreground font-mono">{sub.studioSlug}</span>
              <span className={`text-xs px-1.5 py-0.5 rounded-full font-medium ${statusClass}`}>
                {sub.status}
              </span>
            </div>
            <p className="text-xs text-muted-foreground">
              {sub.planName ?? "No plan"}
              {" · "}
              Trial ends {new Date(sub.trialExpiresAt).toLocaleDateString("en-GB")}
              {" · "}
              Period end {new Date(sub.currentPeriodEnd).toLocaleDateString("en-GB")}
            </p>
          </div>

          <div className="flex items-center gap-2 shrink-0">
            {sub.status !== "Active" && !extending && !activating && !confirming && (
              <Button size="sm" variant="outline" className="h-7 text-xs"
                onClick={() => setExtending(true)}>
                Extend trial
              </Button>
            )}
            {canActivate && !activating && !extending && !confirming && (
              <Button size="sm" variant="outline" className="h-7 text-xs gap-1"
                onClick={() => setActivating(true)}>
                <Banknote className="h-3.5 w-3.5" />
                Activate — Cash Payment
              </Button>
            )}
            {canCancel && !confirming && !extending && !activating && (
              <Button
                size="sm"
                variant="outline"
                className="h-7 text-xs text-destructive border-destructive/40 hover:bg-destructive/10 hover:text-destructive"
                onClick={() => setConfirming(true)}
              >
                Cancel subscription
              </Button>
            )}
          </div>
        </div>

        {extending && (
          <div className="flex items-center gap-2 pt-1">
            <Input
              type="number"
              min="1"
              max="90"
              value={days}
              onChange={(e) => setDays(e.target.value)}
              className="h-7 w-20 text-xs"
            />
            <span className="text-xs text-muted-foreground">days</span>
            <Button size="sm" className="h-7 px-2 text-xs" disabled={extending_} onClick={handleExtend}>
              {extending_ ? <Loader2 className="h-3 w-3 animate-spin" /> : "Confirm"}
            </Button>
            <Button size="sm" variant="ghost" className="h-7 px-2 text-xs"
              onClick={() => setExtending(false)}>
              Cancel
            </Button>
          </div>
        )}

        {activating && (
          <div className="pt-2 space-y-2 border-t">
            <p className="text-xs font-medium text-muted-foreground">Activate — Cash Payment</p>
            <div className="space-y-1">
              <Label htmlFor={`plan-${sub.studioId}`} className="text-xs">Plan</Label>
              <select
                id={`plan-${sub.studioId}`}
                value={cashPlanId}
                onChange={(e) => setCashPlanId(e.target.value)}
                className="h-8 w-full rounded-md border border-input bg-background px-2 text-xs"
              >
                <option value="">Select a plan…</option>
                {plans.map((p) => (
                  <option key={p.id} value={p.id}>{p.name}</option>
                ))}
              </select>
            </div>
            <div className="space-y-1">
              <Label htmlFor={`note-${sub.studioId}`} className="text-xs">Note (optional)</Label>
              <Input
                id={`note-${sub.studioId}`}
                value={cashNote}
                onChange={(e) => setCashNote(e.target.value)}
                placeholder="e.g. Cash paid in person on 2026-06-11"
                className="h-8 text-xs"
              />
            </div>
            <div className="flex gap-2">
              <Button
                size="sm"
                className="h-7 px-2 text-xs flex-1"
                disabled={activating_ || !cashPlanId}
                onClick={handleActivate}
              >
                {activating_
                  ? <Loader2 className="h-3 w-3 animate-spin" />
                  : "Activate subscription"}
              </Button>
              <Button size="sm" variant="ghost" className="h-7 px-2 text-xs"
                onClick={() => { setActivating(false); setCashNote(""); setCashPlanId(""); }}>
                Cancel
              </Button>
            </div>
          </div>
        )}
        {confirming && (
          <div className="flex items-center gap-2 pt-1">
            <span className="text-xs text-destructive font-medium">
              Cancel this subscription?
            </span>
            <Button
              size="sm"
              variant="destructive"
              className="h-7 px-2 text-xs"
              disabled={cancelling_}
              onClick={handleCancel}
            >
              {cancelling_ ? <Loader2 className="h-3 w-3 animate-spin" /> : "Confirm"}
            </Button>
            <Button
              size="sm"
              variant="ghost"
              className="h-7 px-2 text-xs"
              onClick={() => setConfirming(false)}
            >
              Back
            </Button>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

const ALL_STATUSES = ["Active", "Trialing", "GracePeriod", "PastDue", "Cancelled", "NoSubscription"];

export function SubscriptionOversightPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const statusFilter = searchParams.get("status") ?? "";

  // Refetch on mount so the issuer always sees current subscription state.
  const { data: subscriptions, isLoading, isError } =
    useGetPlatformSubscriptionsQuery(undefined, { refetchOnMountOrArgChange: true });

  const filtered = subscriptions?.filter((s) =>
    statusFilter ? s.status === statusFilter : true
  ) ?? [];

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Receipt className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Subscriptions</span>
        {subscriptions && (
          <span className="text-xs text-muted-foreground ml-1">
            {filtered.length === subscriptions.length
              ? `(${subscriptions.length})`
              : `(${filtered.length} of ${subscriptions.length})`}
          </span>
        )}
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-3">
        {!isLoading && !isError && subscriptions && (
          <div className="flex flex-wrap gap-2 mb-4">
            <button
              onClick={() => setSearchParams({})}
              className={`text-xs px-2.5 py-1 rounded-full border transition-colors ${
                !statusFilter ? "bg-primary text-primary-foreground border-primary" : "hover:bg-muted"
              }`}
            >
              All ({subscriptions.length})
            </button>
            {ALL_STATUSES.map((s) => {
              const count = subscriptions.filter((sub) => sub.status === s).length;
              if (count === 0) return null;
              return (
                <button
                  key={s}
                  onClick={() => setSearchParams({ status: s })}
                  className={`text-xs px-2.5 py-1 rounded-full border transition-colors ${
                    statusFilter === s
                      ? "bg-primary text-primary-foreground border-primary"
                      : "hover:bg-muted"
                  }`}
                >
                  {s} ({count})
                </button>
              );
            })}
          </div>
        )}

        {isLoading && (
          <div className="flex items-center justify-center py-16 gap-2 text-muted-foreground">
            <Loader2 className="h-5 w-5 animate-spin" />
            <span className="text-sm">Loading…</span>
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">Failed to load subscriptions.</p>
        )}

        {!isLoading && !isError && filtered.length === 0 && (
          <p className="text-center text-sm text-muted-foreground py-16">No studios found.</p>
        )}

        {!isLoading && !isError && filtered.map((sub) => (
          <SubscriptionRow key={sub.studioId} sub={sub} />
        ))}
      </main>
    </div>
  );
}
