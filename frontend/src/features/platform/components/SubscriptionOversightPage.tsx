import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import {
  AlertTriangle,
  Banknote,
  ChevronLeft,
  ChevronRight,
  Clock,
  Copy,
  ExternalLink,
  Loader2,
  Receipt,
  Search,
  XCircle,
} from "lucide-react";
import { toast } from "sonner";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
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
  Suspended:      "bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-300",
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
  Suspended:      "Suspended",
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
    try {
      await extendTrial({ studioId: sub.studioId, additionalDays }).unwrap();
      toast.success(`Trial extended by ${additionalDays} day${additionalDays !== 1 ? "s" : ""}`);
      setExtending(false);
    } catch {
      toast.error("Failed to extend trial");
    }
  }

  async function handleActivate() {
    if (!cashPlanId) return;
    try {
      await activateManually({
        studioId: sub.studioId,
        planId:   cashPlanId,
        note:     cashNote || undefined,
      }).unwrap();
      toast.success("Subscription activated");
    } catch {
      toast.error("Failed to activate subscription");
    }
    setActivating(false);
    setCashNote("");
    setCashPlanId("");
  }

  const effectiveStatus = sub.isSuspended ? "Suspended" : sub.status;
  const statusClass     = STATUS_CLASSES[effectiveStatus] ?? STATUS_CLASSES.NoSubscription;
  const canActivate      = CASH_ACTIVATABLE.has(sub.status);
  const canCancel        = CANCELLABLE.has(sub.status);

  const trialExpired = sub.trialExpiresAt ? new Date(sub.trialExpiresAt) < new Date() : false;
  const isTrialRelevantState =
    effectiveStatus === "Trialing" || effectiveStatus === "GracePeriod" || effectiveStatus === "NoSubscription";

  const planDisplay = (() => {
    if (sub.status === "Trialing") return "In Trial";
    if (sub.status === "NoSubscription") return "No subscription";
    return sub.planName ?? "No plan assigned";
  })();
  const showNoPlanWarning =
    sub.status !== "Trialing" && sub.status !== "NoSubscription" && !sub.planName;

  const periodText = (() => {
    if (sub.status === "Active")       return `Renews: ${fmt(sub.currentPeriodEnd)}`;
    if (sub.status === "GracePeriod")  return `Grace ends: ${fmt(sub.currentPeriodEnd)}`;
    if (sub.status === "PastDue")      return `Overdue since: ${fmt(sub.currentPeriodEnd)}`;
    if (sub.status === "Cancelled")    return `Cancelled — expired ${fmt(sub.currentPeriodEnd)}`;
    return null;
  })();

  async function handleCancel() {
    try {
      await cancelSub(sub.studioId).unwrap();
      toast.success("Subscription cancelled");
      setConfirming(false);
    } catch {
      toast.error("Failed to cancel subscription");
    }
  }

  return (
    <Card className={sub.isSuspended ? "border-amber-400/40 dark:border-amber-600/30" : ""}>
      <CardContent className="p-4 space-y-2">
        <div className="flex items-start justify-between gap-4">
          <div className="space-y-0.5 min-w-0">
            <div className="flex items-center gap-2 flex-nowrap min-w-0">
              <span className="font-medium text-sm shrink-0">{sub.studioName}</span>
              <button
                type="button"
                onClick={(e) => {
                  e.stopPropagation();
                  void navigator.clipboard.writeText(sub.studioSlug);
                  toast.success("Slug copied");
                }}
                aria-label={`Copy slug ${sub.studioSlug}`}
                className="group flex items-center gap-0.5 text-xs text-muted-foreground
                           font-mono hover:text-foreground transition-colors cursor-pointer
                           max-w-[180px]"
              >
                <span className="truncate" title={sub.studioSlug}>{sub.studioSlug}</span>
                <Copy className="h-2.5 w-2.5 shrink-0 opacity-0 group-hover:opacity-50
                                 transition-opacity" />
              </button>
              <span className={`text-xs px-1.5 py-0.5 rounded-full font-medium shrink-0 ${statusClass}`}>
                {STATUS_LABELS[effectiveStatus] ?? effectiveStatus}
              </span>
            </div>
            <div className="space-y-0.5">
              <p className="text-xs text-muted-foreground">
                {planDisplay}
                {showNoPlanWarning && (
                  <span
                    title="Active subscription has no linked plan — check billing data"
                    className="inline-flex items-center ml-1 text-amber-500"
                  >
                    <AlertTriangle className="h-3 w-3" />
                  </span>
                )}
                {sub.trialExpiresAt && isTrialRelevantState && (
                  <>
                    {" · "}
                    {trialExpired ? `Trial expired ${fmt(sub.trialExpiresAt)}` : `Trial ends ${fmt(sub.trialExpiresAt)}`}
                  </>
                )}
              </p>
              {periodText && (
                <p className="text-xs text-muted-foreground">{periodText}</p>
              )}
            </div>
          </div>

          <div className="flex items-center gap-1.5 shrink-0">
            <Link to={`/platform/studios/${sub.studioId}`}>
              <Button
                size="sm"
                variant="ghost"
                className="h-7 px-2 text-xs gap-1"
                aria-label={`View ${sub.studioName} studio details`}
              >
                <ExternalLink className="h-3.5 w-3.5" />
                View
              </Button>
            </Link>
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
                {trialExpired ? "Grant Extension (+7 days)" : "Extend Trial (+7 days)"}
              </Button>
            )}
            {canActivate && !activating && !extending && !confirming && (
              <Button
                size="sm"
                variant={effectiveStatus === "Cancelled" ? "outline" : "default"}
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
            <div className="space-y-0.5">
              <p className="text-xs font-medium">Record Cash Payment</p>
              <p className="text-xs text-muted-foreground">
                Manually activates the subscription — use when payment was collected offline.
              </p>
            </div>
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

const ALL_STATUSES = ["Suspended", "Active", "Trialing", "GracePeriod", "PastDue", "Cancelled", "NoSubscription"];

const PAGE_SIZE = 10;

export function SubscriptionOversightPage() {
  useDocumentMeta({ title: "Subscriptions — Platform Admin", canonical: "/platform/subscriptions" });

  const [searchParams, setSearchParams] = useSearchParams();
  const statusFilter = searchParams.get("status") ?? "";
  const [search, setSearch] = useState("");
  const [sortKey, setSortKey] = useState<"name" | "trialEnd" | "periodEnd">("trialEnd");
  const [page, setPage] = useState(1);

  // Refetch on mount so the issuer always sees current subscription state.
  const { data: subscriptions, isLoading, isError } =
    useGetPlatformSubscriptionsQuery(undefined, { refetchOnMountOrArgChange: true });

  const baseFiltered = subscriptions?.filter((s) => {
    const effective = s.isSuspended ? "Suspended" : s.status;
    return statusFilter ? effective === statusFilter : true;
  }) ?? [];

  const q = search.trim().toLowerCase();

  const searched = q
    ? baseFiltered.filter((s) =>
        s.studioName.toLowerCase().includes(q) ||
        s.studioSlug.toLowerCase().includes(q)
      )
    : baseFiltered;

  const filtered = [...searched].sort((a, b) => {
    if (sortKey === "name")      return a.studioName.localeCompare(b.studioName);
    if (sortKey === "trialEnd") {
      const aTime = a.trialExpiresAt ? new Date(a.trialExpiresAt).getTime() : Infinity;
      const bTime = b.trialExpiresAt ? new Date(b.trialExpiresAt).getTime() : Infinity;
      return aTime - bTime;
    }
    if (sortKey === "periodEnd") return new Date(a.currentPeriodEnd).getTime() - new Date(b.currentPeriodEnd).getTime();
    return 0;
  });

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const pageSubs   = filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE);

  // Clamp the current page if filtering shrinks the result set below it.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setPage((p) => Math.min(p, totalPages));
  }, [totalPages]);

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-20">
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

      <main className="max-w-5xl mx-auto px-4 py-6 space-y-3">
        {/* ── Search + sort toolbar ────────────────────────────────────── */}
        <div className="flex gap-2 flex-wrap mb-3">
          <div className="relative flex-1 min-w-48">
            <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5
                               text-muted-foreground pointer-events-none" />
            <Input
              aria-label="Search subscriptions by studio name or slug"
              placeholder="Search by studio name or slug…"
              value={search}
              onChange={(e) => { setSearch(e.target.value); setPage(1); }}
              className="pl-8 h-8 text-sm"
            />
          </div>
          <select
            value={sortKey}
            onChange={(e) => { setSortKey(e.target.value as typeof sortKey); setPage(1); }}
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
              onClick={() => { setSearchParams({}); setPage(1); }}
              className={`text-xs px-2.5 py-1 rounded-full border transition-colors ${
                !statusFilter ? "bg-primary text-primary-foreground border-primary" : "hover:bg-muted"
              }`}
            >
              All ({subscriptions.length})
            </button>
            {ALL_STATUSES.map((s) => {
              const count = subscriptions.filter(
                (sub) => (sub.isSuspended ? "Suspended" : sub.status) === s,
              ).length;
              if (count === 0) return null;
              return (
                <button
                  key={s}
                  onClick={() => { setSearchParams({ status: s }); setPage(1); }}
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
                  setPage(1);
                }}
              >
                Clear filters
              </Button>
            )}
          </div>
        )}

        {!isLoading && !isError && pageSubs.map((sub) => (
          <SubscriptionRow key={sub.studioId} sub={sub} />
        ))}

        {!isLoading && !isError && totalPages > 1 && (
          <div className="flex items-center justify-between pt-2">
            <Button
              size="sm" variant="outline" className="h-7 text-xs gap-1"
              disabled={page === 1}
              onClick={() => setPage((p) => p - 1)}
            >
              <ChevronLeft className="h-3.5 w-3.5" />
              Previous
            </Button>
            <span className="text-xs text-muted-foreground">
              Page {page} of {totalPages}
            </span>
            <Button
              size="sm" variant="outline" className="h-7 text-xs gap-1"
              disabled={page === totalPages}
              onClick={() => setPage((p) => p + 1)}
            >
              Next
              <ChevronRight className="h-3.5 w-3.5" />
            </Button>
          </div>
        )}
      </main>
    </div>
  );
}
