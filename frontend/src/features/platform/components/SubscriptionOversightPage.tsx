import { useState } from "react";
import { useSearchParams } from "react-router-dom";
import { Banknote, Clock, Loader2, Receipt, Search, XCircle } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Skeleton } from "@/shared/components/ui/skeleton";
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

function fmt(date: string): string {
  return new Date(date).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

const STATUS_LABELS: Record<string, string> = {
  Active:         "Active",
  Trialing:       "In Trial",
  PastDue:        "Past Due",
  GracePeriod:    "Grace Period",
  Cancelled:      "Cancelled",
  NoSubscription: "No Subscription",
};

function SubscriptionRowSkeleton() {
  return (
    <Card>
      <CardContent className="p-4 space-y-2">
        <div className="flex items-start justify-between gap-4">
          <div className="space-y-1.5 flex-1">
            <div className="flex items-center gap-2">
              <Skeleton className="h-4 w-32" />
              <Skeleton className="h-3 w-28" />
              <Skeleton className="h-5 w-20 rounded-full" />
            </div>
            <Skeleton className="h-3 w-56" />
            <Skeleton className="h-3 w-40" />
          </div>
          <div className="flex items-center gap-1.5">
            <Skeleton className="h-7 w-28" />
            <Skeleton className="h-7 w-20" />
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

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

  const trialExpired = new Date(sub.trialExpiresAt) < new Date();

  const periodText = (() => {
    if (sub.status === "Active")       return `Renews: ${fmt(sub.currentPeriodEnd)}`;
    if (sub.status === "GracePeriod")  return `Grace ends: ${fmt(sub.currentPeriodEnd)}`;
    if (sub.status === "PastDue")      return `Overdue since: ${fmt(sub.currentPeriodEnd)}`;
    if (sub.status === "Cancelled")    return `Cancelled — expired ${fmt(sub.currentPeriodEnd)}`;
    return null;
  })();

  async function handleCancel() {
    await cancelSub(sub.studioId).unwrap();
    setConfirming(false);
  }

  return (
    <Card>
      <CardContent className="p-4 space-y-2">
        <div className="flex items-start justify-between gap-4">
          <div className="space-y-0.5 min-w-0">
            <div className="flex items-center gap-2 flex-nowrap min-w-0">
              <span className="font-medium text-sm shrink-0">{sub.studioName}</span>
              <span className="text-xs text-muted-foreground font-mono truncate max-w-[180px]"
                    title={sub.studioSlug}>
                {sub.studioSlug}
              </span>
              <span className={`text-xs px-1.5 py-0.5 rounded-full font-medium shrink-0 ${statusClass}`}>
                {STATUS_LABELS[sub.status] ?? sub.status}
              </span>
            </div>
            <div className="space-y-0.5">
              <p className="text-xs text-muted-foreground">
                {sub.status === "Trialing" ? "In Trial" : (sub.planName ?? "No paid plan")}
                {" · "}
                {trialExpired ? `Trial expired ${fmt(sub.trialExpiresAt)}` : `Trial ends ${fmt(sub.trialExpiresAt)}`}
              </p>
              {periodText && (
                <p className="text-xs text-muted-foreground">{periodText}</p>
              )}
            </div>
          </div>

          <div className="flex items-center gap-1.5 shrink-0">
            {sub.status !== "Active" && !extending && !activating && !confirming && (
              <Button
                size="sm"
                variant="outline"
                className="h-7 text-xs gap-1"
                onClick={() => setExtending(true)}
                aria-label={trialExpired
                  ? `Grant extension for ${sub.studioName}`
                  : `Extend trial for ${sub.studioName}`}
              >
                <Clock className="h-3.5 w-3.5" />
                {trialExpired ? "Grant Extension" : "Extend Trial (+7 days)"}
              </Button>
            )}
            {canActivate && !activating && !extending && !confirming && (
              <Button
                size="sm"
                className="h-7 text-xs gap-1"
                onClick={() => setActivating(true)}
                aria-label={`Activate subscription for ${sub.studioName}`}
              >
                <Banknote className="h-3.5 w-3.5" />
                Activate
              </Button>
            )}
            {canCancel && !confirming && !extending && !activating && (
              <Button
                size="sm"
                variant="outline"
                className="h-7 text-xs gap-1 text-destructive border-destructive/40
                           hover:bg-destructive/10 hover:text-destructive"
                onClick={() => setConfirming(true)}
                aria-label={`Cancel subscription for ${sub.studioName}`}
              >
                <XCircle className="h-3.5 w-3.5" />
                Cancel Subscription
              </Button>
            )}
          </div>
        </div>

        {extending && (
          <div className="flex items-center gap-2 pt-2 border-t">
            <span className="text-xs text-muted-foreground">
              {trialExpired ? "Grant extension of" : "Extend trial by"}
            </span>
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
              Cancel subscription for <strong>{sub.studioName}</strong>?
            </span>
            <Button
              size="sm"
              variant="destructive"
              className="h-7 px-2 text-xs"
              disabled={cancelling_}
              onClick={handleCancel}
            >
              {cancelling_ ? <Loader2 className="h-3 w-3 animate-spin" /> : "Yes, cancel"}
            </Button>
            <Button
              size="sm"
              variant="ghost"
              className="h-7 px-2 text-xs"
              onClick={() => setConfirming(false)}
            >
              Keep
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
  const [search, setSearch] = useState("");
  const [sortKey, setSortKey] = useState<"name" | "trialEnd" | "periodEnd">("trialEnd");

  // Refetch on mount so the issuer always sees current subscription state.
  const { data: subscriptions, isLoading, isError } =
    useGetPlatformSubscriptionsQuery(undefined, { refetchOnMountOrArgChange: true });

  const baseFiltered = subscriptions?.filter((s) =>
    statusFilter ? s.status === statusFilter : true
  ) ?? [];

  const q = search.trim().toLowerCase();

  const searched = q
    ? baseFiltered.filter((s) =>
        s.studioName.toLowerCase().includes(q) ||
        s.studioSlug.toLowerCase().includes(q)
      )
    : baseFiltered;

  const filtered = [...searched].sort((a, b) => {
    if (sortKey === "name")      return a.studioName.localeCompare(b.studioName);
    if (sortKey === "trialEnd")  return new Date(a.trialExpiresAt).getTime() - new Date(b.trialExpiresAt).getTime();
    if (sortKey === "periodEnd") return new Date(a.currentPeriodEnd).getTime() - new Date(b.currentPeriodEnd).getTime();
    return 0;
  });

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
        {/* ── Search + sort toolbar ────────────────────────────────────── */}
        <div className="flex gap-2 flex-wrap mb-3">
          <div className="relative flex-1 min-w-48">
            <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5
                               text-muted-foreground pointer-events-none" />
            <Input
              placeholder="Search by studio name or slug…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="pl-8 h-8 text-sm"
            />
          </div>
          <select
            value={sortKey}
            onChange={(e) => setSortKey(e.target.value as typeof sortKey)}
            className="h-8 rounded-md border border-input bg-background px-2 text-xs"
            aria-label="Sort subscriptions"
          >
            <option value="trialEnd">Trial end (soonest first)</option>
            <option value="periodEnd">Period end (soonest first)</option>
            <option value="name">Studio name (A–Z)</option>
          </select>
        </div>

        {subscriptions && (
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
                  {STATUS_LABELS[s] ?? s} ({count})
                </button>
              );
            })}
          </div>
        )}

        {isLoading && (
          <div className="space-y-3">
            {[1, 2, 3, 4, 5].map((i) => <SubscriptionRowSkeleton key={i} />)}
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">Failed to load subscriptions.</p>
        )}

        {!isLoading && !isError && filtered.length === 0 && (
          <div className="flex flex-col items-center justify-center py-24 gap-3">
            <Receipt className="h-10 w-10 text-muted-foreground/30" />
            <p className="text-sm text-muted-foreground">
              {subscriptions?.length === 0
                ? "No subscriptions yet."
                : q && statusFilter
                  ? `No ${STATUS_LABELS[statusFilter] ?? statusFilter} subscriptions matching "${search}".`
                  : q
                    ? `No subscriptions matching "${search}".`
                    : `No ${STATUS_LABELS[statusFilter] ?? statusFilter} subscriptions.`}
            </p>
            {(q || statusFilter) && (
              <Button
                size="sm"
                variant="outline"
                className="text-xs"
                onClick={() => {
                  setSearch("");
                  setSearchParams({});
                }}
              >
                Clear filters
              </Button>
            )}
          </div>
        )}

        {!isLoading && !isError && filtered.map((sub) => (
          <SubscriptionRow key={sub.studioId} sub={sub} />
        ))}
      </main>
    </div>
  );
}
