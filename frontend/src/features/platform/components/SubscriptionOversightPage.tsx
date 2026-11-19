import { useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import {
  AlertTriangle,
  Banknote,
  BellOff,
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
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  useGetPlatformSubscriptionsQuery,
  useSetDunningExclusionMutation,
} from "@/features/platform/platformApi";
import { useGetAdminPlansQuery } from "@/features/billing/billingApi";
import { DataMaintenanceCard } from "./DataMaintenanceCard";
import { ActivationReceiptNotice, CashActivationForm, type ActivationReceipt } from "./CashActivationForm";
import { CancelSubscriptionReview } from "./CancelSubscriptionReview";
import { ExtendTrialReview } from "./ExtendTrialReview";
import { CASH_ACTIVATABLE } from "./subscriptionKnowledge";
import type { PlatformSubscriptionResponse } from "@/features/platform/platform.types";

const STATUS_CLASSES: Record<string, string> = {
  Active:         "bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-300",
  Trialing:       "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300",
  PastDue:        "bg-yellow-100 text-yellow-800 dark:bg-yellow-900/30 dark:text-yellow-300",
  GracePeriod:    "bg-orange-100 text-orange-700 dark:bg-orange-900/30 dark:text-orange-300",
  Cancelled:      "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300",
  NoSubscription: "bg-muted text-muted-foreground",
  Suspended:      "bg-amber-100 text-amber-800 dark:bg-amber-900/30 dark:text-amber-300",
};

function fmt(date: string): string {
  return new Date(date).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

function daysPastDue(pastDueSince?: string | null): number | null {
  if (!pastDueSince) return null;
  return Math.max(0, Math.floor((Date.now() - new Date(pastDueSince).getTime()) / (1000 * 60 * 60 * 24)));
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
  /** The list is being re-read: a failed activation's retry waits for it (D04). */
  checking: boolean;
  onRecheck: () => void;
}
const CANCELLABLE      = new Set(["Active", "PastDue", "Trialing", "GracePeriod"]);

function SubscriptionRow({ sub, checking, onRecheck }: SubscriptionRowProps) {
  const [extending,   setExtending]   = useState(false);
  const [activating,  setActivating]  = useState(false);
  const [confirming,  setConfirming]  = useState(false);
  const [receipt,     setReceipt]     = useState<ActivationReceipt | null>(null);

  const { data: plans = [] } = useGetAdminPlansQuery();

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

  const overdueDays = sub.status === "PastDue" ? daysPastDue(sub.pastDueSince) : null;

  const periodText = (() => {
    if (sub.status === "Active" && sub.cancelAtPeriodEnd) return `Cancels: ${fmt(sub.currentPeriodEnd)}`;
    if (sub.status === "Active")       return `Renews: ${fmt(sub.currentPeriodEnd)}`;
    if (sub.status === "GracePeriod")  return `Grace ends: ${fmt(sub.currentPeriodEnd)}`;
    if (sub.status === "PastDue") {
      const overdueSuffix = overdueDays !== null ? ` · ${overdueDays} day${overdueDays === 1 ? "" : "s"} overdue` : "";
      return `Overdue since: ${fmt(sub.currentPeriodEnd)}${overdueSuffix}`;
    }
    if (sub.status === "Cancelled")    return `Cancelled — expired ${fmt(sub.currentPeriodEnd)}`;
    return null;
  })();

  const [setDunningExclusion, { isLoading: settingExclusion }] = useSetDunningExclusionMutation();

  async function handleToggleDunningExclusion() {
    try {
      await setDunningExclusion({ studioId: sub.studioId, excluded: !sub.dunningExcludedManually }).unwrap();
      toast.success(sub.dunningExcludedManually
        ? "Dunning reminders re-enabled for this studio"
        : "Dunning reminders excluded for this studio");
    } catch {
      toast.error("Failed to update dunning exclusion");
    }
  }

  return (
    <Card className={sub.isSuspended ? "border-amber-400/40 dark:border-amber-600/30" : ""}>
      <CardContent className="p-4 space-y-2">
        {/* Stacked on phones, side by side from sm up: the action group below is wider than a phone
            and must be free to wrap there, or it pushes the whole page past the viewport. */}
        <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between sm:gap-4">
          <div className="space-y-0.5 min-w-0">
            <div className="flex items-center gap-x-2 gap-y-1 flex-wrap min-w-0">
              <span className="font-medium text-sm">{sub.studioName}</span>
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
                    className="inline-flex items-center ml-1 text-warning-text"
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

          <div className="flex items-center gap-1.5 flex-wrap sm:justify-end sm:shrink-0">
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
                className="h-7 text-xs gap-1 text-destructive-text border-destructive/40
                           hover:bg-destructive/10 hover:text-destructive-text"
                onClick={() => setConfirming(true)}
                aria-label={`Cancel subscription for ${sub.studioName}`}
              >
                <XCircle className="h-3.5 w-3.5" />
                Cancel Subscription
              </Button>
            )}
            {sub.status === "PastDue" && (
              <Button
                size="sm"
                variant="outline"
                className="h-7 text-xs gap-1"
                disabled={settingExclusion}
                onClick={handleToggleDunningExclusion}
                aria-label={sub.dunningExcludedManually
                  ? `Re-enable dunning reminders for ${sub.studioName}`
                  : `Exclude ${sub.studioName} from dunning reminders`}
              >
                {settingExclusion ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <BellOff className="h-3.5 w-3.5" />}
                {sub.dunningExcludedManually ? "Re-enable Reminders" : "Exclude From Dunning"}
              </Button>
            )}
          </div>
        </div>

        {/* The shared consequence reviews (D03): the same shape here as on the studio list and detail pages. */}
        {extending && (
          <ExtendTrialReview
            studioId={sub.studioId}
            studioName={sub.studioName}
            idSuffix={sub.studioId}
            status={sub.status}
            currentTrialEnd={sub.trialExpiresAt ?? null}
            onDone={() => setExtending(false)}
            onBack={() => setExtending(false)}
          />
        )}

        {activating && (
          <CashActivationForm
            studioId={sub.studioId}
            studioName={sub.studioName}
            plans={plans}
            idSuffix={sub.studioId}
            notePlaceholder="e.g. Cash paid in person on 2026-06-11"
            status={sub.status}
            knowledge={{ kind: "known", status: sub.status }}
            checking={checking}
            onRecheck={onRecheck}
            onClose={() => setActivating(false)}
            onActivated={setReceipt}
          />
        )}
        {receipt && <ActivationReceiptNotice receipt={receipt} onDismiss={() => setReceipt(null)} />}
        {confirming && (
          <CancelSubscriptionReview
            studioId={sub.studioId}
            studioName={sub.studioName}
            idSuffix={sub.studioId}
            onDone={() => setConfirming(false)}
            onBack={() => setConfirming(false)}
          />
        )}
      </CardContent>
    </Card>
  );
}

const ALL_STATUSES = ["Suspended", "Active", "Trialing", "GracePeriod", "PastDue", "Cancelled", "NoSubscription"];

const PAGE_SIZE = 10;

const SORT_KEYS = ["trialEnd", "periodEnd", "daysOverdue", "name"] as const;
type SortKey = (typeof SORT_KEYS)[number];

export function SubscriptionOversightPage() {
  useDocumentMeta({ title: "Subscriptions — Platform Admin", canonical: "/platform/subscriptions" });

  // Status, sort and page live in the URL (D08), so Back, a reload or a shared link restores the view. The
  // search box stays in memory: an admin's search text can be a person's name, and nothing private goes in
  // a URL (the C08 rule).
  const [searchParams, setSearchParams] = useSearchParams();
  const statusFilter = searchParams.get("status") ?? "";
  const sortKey: SortKey = SORT_KEYS.find((k) => k === searchParams.get("sort")) ?? "trialEnd";
  const [search, setSearchText] = useState("");

  // A filter or sort change returns to page 1 and replaces the history entry; a page change pushes one.
  function updateParams(change: (next: URLSearchParams) => void, replace: boolean) {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      change(next);
      return next;
    }, { replace });
  }
  const setStatus = (status: string) => updateParams((n) => {
    if (status) n.set("status", status); else n.delete("status");
    n.delete("page");
  }, true);
  const setSortKey = (key: SortKey) => updateParams((n) => {
    if (key === "trialEnd") n.delete("sort"); else n.set("sort", key);
    n.delete("page");
  }, true);
  const setSearch = (value: string) => {
    setSearchText(value);
    updateParams((n) => n.delete("page"), true);
  };
  const setPage = (nextPage: number) => updateParams((n) => {
    if (nextPage > 1) n.set("page", String(nextPage)); else n.delete("page");
  }, false);

  // Refetch on mount so the admin always sees current subscription state.
  const { data: subscriptions, isLoading, isError, isFetching, refetch } =
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
    if (sortKey === "daysOverdue") {
      // Studios not currently overdue sort to the end regardless of direction.
      const aDays = daysPastDue(a.pastDueSince) ?? -1;
      const bDays = daysPastDue(b.pastDueSince) ?? -1;
      return bDays - aDays;
    }
    return 0;
  });

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  // Derived, not synced: the URL's page, clamped when the filters shrink the list below it.
  const page       = Math.min(Math.max(1, Number.parseInt(searchParams.get("page") ?? "1", 10) || 1), totalPages);
  const pageSubs   = filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE);

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-[var(--admin-nav-height)] z-10">
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
              onChange={(e) => setSearch(e.target.value)}
              className="pl-8 h-8 text-sm"
            />
          </div>
          <select
            value={sortKey}
            onChange={(e) => setSortKey(e.target.value as SortKey)}
            className="h-8 rounded-md border border-input bg-background px-2 text-xs"
            aria-label="Sort subscriptions"
          >
            <option value="trialEnd">Trial end (soonest first)</option>
            <option value="periodEnd">Period end (soonest first)</option>
            <option value="daysOverdue">Days overdue (most first)</option>
            <option value="name">Studio name (A–Z)</option>
          </select>
        </div>

        {subscriptions && (
          <div className="flex flex-wrap gap-2 mb-4">
            <button
              onClick={() => setStatus("")}
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
                  onClick={() => setStatus(s)}
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
          <p className="text-center text-sm text-destructive-text py-16">Failed to load subscriptions.</p>
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
                  setSearchText("");
                  setSearchParams({}, { replace: true });
                }}
              >
                Clear filters
              </Button>
            )}
          </div>
        )}

        {!isLoading && !isError && pageSubs.map((sub) => (
          <SubscriptionRow key={sub.studioId} sub={sub} checking={isFetching} onRecheck={() => void refetch()} />
        ))}

        {!isLoading && !isError && totalPages > 1 && (
          <div className="flex items-center justify-between pt-2">
            <Button
              size="sm" variant="outline" className="h-7 text-xs gap-1"
              disabled={page === 1}
              onClick={() => setPage(page - 1)}
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
              onClick={() => setPage(page + 1)}
            >
              Next
              <ChevronRight className="h-3.5 w-3.5" />
            </Button>
          </div>
        )}

        <DataMaintenanceCard />
      </main>
    </div>
  );
}
