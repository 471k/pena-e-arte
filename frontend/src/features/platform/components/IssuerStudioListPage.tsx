import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { toast } from "sonner";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import {
  AlertTriangle,
  Banknote,
  Building2,
  ChevronLeft,
  ChevronRight,
  Clock,
  Copy,
  ExternalLink,
  Loader2,
  PauseCircle,
  PlayCircle,
  Search,
  XCircle,
} from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  useGetStudiosQuery,
  useSuspendStudioMutation,
  useUnsuspendStudioMutation,
} from "@/features/studios/studiosApi";
import type { StudioResponse } from "@/features/studios/studiosApi";
import {
  useGetPlatformSubscriptionsQuery,
  useExtendTrialMutation,
  useActivateSubscriptionManuallyMutation,
  useCancelSubscriptionMutation,
} from "@/features/platform/platformApi";
import type { PlatformSubscriptionResponse } from "@/features/platform/platform.types";
import { useGetIssuerPlansQuery } from "@/features/billing/billingApi";
import type { PlanResponse } from "@/features/billing/billing.types";

// ── Status display config ──────────────────────────────────────────────────────

const STATUS_CLASSES: Record<string, string> = {
  Active:         "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300",
  Trialing:       "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300",
  PastDue:        "bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-300",
  GracePeriod:    "bg-orange-100 text-orange-700 dark:bg-orange-900/30 dark:text-orange-300",
  Cancelled:      "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300",
  NoSubscription: "bg-muted text-muted-foreground",
  Suspended:      "bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-300",
};

const STATUS_LABELS: Record<string, string> = {
  Active:         "Active",
  Trialing:       "In Trial",
  PastDue:        "Past Due",
  GracePeriod:    "Grace Period",
  Cancelled:      "Cancelled",
  NoSubscription: "No Subscription",
  Suspended:      "Suspended",
};

const CASH_ACTIVATABLE = new Set(["NoSubscription", "PastDue", "GracePeriod", "Cancelled"]);
const CANCELLABLE      = new Set(["Active", "PastDue", "Trialing", "GracePeriod"]);

const STATUS_SORT_ORDER: Record<string, number> = {
  Suspended:      0,
  PastDue:        1,
  GracePeriod:    2,
  Trialing:       3,
  Active:         4,
  NoSubscription: 5,
  Cancelled:      6,
};

const ALL_FILTER_STATUSES = [
  "Active", "Trialing", "GracePeriod", "PastDue",
  "Cancelled", "NoSubscription", "Suspended",
] as const;

const PAGE_SIZE = 10;

function fmt(date: string) {
  return new Date(date).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

// ── Skeleton ──────────────────────────────────────────────────────────────────

function StudioRowSkeleton() {
  return (
    <Card>
      <CardContent className="p-4 space-y-2">
        <div className="flex items-start justify-between gap-4">
          <div className="space-y-1.5 flex-1">
            <div className="flex items-center gap-2">
              <Skeleton className="h-4 w-32" />
              <Skeleton className="h-3 w-28" />
              <Skeleton className="h-5 w-16 rounded-full" />
            </div>
            <Skeleton className="h-3 w-64" />
          </div>
          <div className="flex items-center gap-1.5">
            <Skeleton className="h-7 w-20" />
            <Skeleton className="h-7 w-16" />
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

// ── Row ───────────────────────────────────────────────────────────────────────

interface StudioRowProps {
  studio: StudioResponse;
  sub:    PlatformSubscriptionResponse | undefined;
  plans:  PlanResponse[];
}

function StudioRow({ studio, sub, plans }: StudioRowProps) {
  const isSuspended = !studio.isActive;
  const subStatus   = sub?.status ?? "NoSubscription";
  const badgeStatus = isSuspended ? "Suspended" : subStatus;

  // Platform actions
  const [confirmPlatform, setConfirmPlatform] = useState<"suspend" | "unsuspend" | null>(null);
  const [suspend,   { isLoading: suspending   }] = useSuspendStudioMutation();
  const [unsuspend, { isLoading: unsuspending }] = useUnsuspendStudioMutation();

  // Subscription actions
  const [extending,  setExtending]  = useState(false);
  const [activating, setActivating] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [days,       setDays]       = useState("7");
  const [cashPlanId, setCashPlanId] = useState("");
  const [cashNote,   setCashNote]   = useState("");

  const [extendTrial,      { isLoading: extending_  }] = useExtendTrialMutation();
  const [activateManually, { isLoading: activating_ }] = useActivateSubscriptionManuallyMutation();
  const [cancelSub,        { isLoading: cancelling_ }] = useCancelSubscriptionMutation();

  const canExtendTrial = subStatus !== "Active";
  const canActivate    = CASH_ACTIVATABLE.has(subStatus);
  const canCancel      = CANCELLABLE.has(subStatus);
  const anyExpanded    = extending || activating || confirming || confirmPlatform !== null;

  const trialDate    = sub?.trialExpiresAt ?? studio.trialExpiresAt;
  const trialExpired = new Date(trialDate) < new Date();

  async function executePlatform() {
    try {
      if (confirmPlatform === "suspend")   await suspend(studio.id).unwrap();
      if (confirmPlatform === "unsuspend") await unsuspend(studio.id).unwrap();
      toast.success(confirmPlatform === "suspend" ? "Studio suspended" : "Studio reinstated");
    } catch {
      toast.error(confirmPlatform === "suspend" ? "Failed to suspend studio" : "Failed to reinstate studio");
    } finally {
      setConfirmPlatform(null);
    }
  }

  async function handleExtend() {
    const d = parseInt(days, 10);
    if (isNaN(d) || d < 1) return;
    try {
      await extendTrial({ studioId: studio.id, additionalDays: d }).unwrap();
      toast.success(`Trial extended by ${d} day${d !== 1 ? "s" : ""}`);
      setExtending(false);
    } catch {
      toast.error("Failed to extend trial");
    }
  }

  async function handleActivate() {
    if (!cashPlanId) return;
    try {
      await activateManually({ studioId: studio.id, planId: cashPlanId, note: cashNote || undefined }).unwrap();
      toast.success("Subscription activated");
    } catch {
      toast.error("Failed to activate subscription");
    }
    setActivating(false);
    setCashPlanId("");
    setCashNote("");
  }

  async function handleCancel() {
    try {
      await cancelSub(studio.id).unwrap();
      toast.success("Subscription cancelled");
      setConfirming(false);
    } catch {
      toast.error("Failed to cancel subscription");
    }
  }

  const planDisplay = (() => {
    if (subStatus === "Trialing") return "In Trial";
    if (subStatus === "NoSubscription") return "No subscription";
    return sub?.planName ?? "No plan assigned";
  })();

  const periodText = (() => {
    if (sub?.status === "Active" && sub?.currentPeriodEnd && !isSuspended && sub?.cancelAtPeriodEnd) {
      return `Cancels: ${fmt(sub.currentPeriodEnd)}`;
    }
    if (sub?.status === "Active" && sub?.currentPeriodEnd && !isSuspended) {
      return `Renews: ${fmt(sub.currentPeriodEnd)}`;
    }
    if (sub?.status === "GracePeriod") {
      return `Grace ends: ${fmt(sub.currentPeriodEnd)}`;
    }
    if (sub?.status === "PastDue" && sub?.currentPeriodEnd) {
      return `Overdue since: ${fmt(sub.currentPeriodEnd)}`;
    }
    if (sub?.status === "Cancelled") {
      return `Cancelled — ended ${sub.currentPeriodEnd ? fmt(sub.currentPeriodEnd) : ""}`.trim();
    }
    // Trial dates only for trial-relevant states
    const isTrialState = !sub || sub.status === "Trialing" || sub.status === "NoSubscription";
    if (isTrialState && trialDate) {
      return trialExpired ? `Trial expired: ${fmt(trialDate)}` : `Trial ends: ${fmt(trialDate)}`;
    }
    return null;
  })();

  return (
    <Card className={isSuspended ? "border-destructive/40" : ""}>
      <CardContent className="p-4 space-y-2">

        {/* ── Main row ─────────────────────────────────────────────────── */}
        <div className="flex items-start justify-between gap-4">
          <div className="space-y-0.5 min-w-0">
            <div className="flex items-center gap-2 flex-nowrap min-w-0">
              <span className="font-medium text-sm shrink-0">{studio.name}</span>
              <button
                type="button"
                onClick={(e) => {
                  e.stopPropagation();
                  void navigator.clipboard.writeText(studio.slug);
                  toast.success("Slug copied");
                }}
                aria-label={`Copy slug ${studio.slug}`}
                className="group flex items-center gap-0.5 text-xs text-muted-foreground
                           font-mono hover:text-foreground transition-colors cursor-pointer
                           max-w-[180px]"
              >
                <span className="truncate" title={studio.slug}>{studio.slug}</span>
                <Copy className="h-2.5 w-2.5 shrink-0 opacity-0 group-hover:opacity-50
                                 transition-opacity" />
              </button>
              <span className={`text-xs px-1.5 py-0.5 rounded-full font-medium shrink-0 ${STATUS_CLASSES[badgeStatus]}`}>
                {STATUS_LABELS[badgeStatus]}
              </span>
            </div>
            <p className="text-xs text-muted-foreground">
              {studio.city}
              {" · "}Registered {fmt(studio.createdAt)}
              {" · "}{planDisplay}
              {subStatus !== "Trialing" && subStatus !== "NoSubscription" && !sub?.planName && (
                <span
                  title="Active subscription has no linked plan — check billing data"
                  className="inline-flex items-center ml-1 text-amber-500"
                >
                  <AlertTriangle className="h-3 w-3" />
                </span>
              )}
              {periodText && <>{" · "}{periodText}</>}
            </p>
          </div>

          {/* ── Action buttons ──────────────────────────────────────────── */}
          <div className="flex items-center gap-1.5 shrink-0 flex-wrap justify-end">

            {/* 0. View detail — always visible, never hidden by anyExpanded */}
            <Link to={`/platform/studios/${studio.id}`}>
              <Button size="sm" variant="ghost" className="h-7 px-2 text-xs gap-1"
                title="View studio details">
                <ExternalLink className="h-3.5 w-3.5" />
                View
              </Button>
            </Link>

            {/* 1. Extend trial */}
            {!anyExpanded && canExtendTrial && (
              <Button size="sm" variant="outline" className="h-7 text-xs gap-1"
                onClick={() => setExtending(true)}>
                <Clock className="h-3.5 w-3.5" />
                {trialExpired ? "Grant Extension (+7 days)" : "Extend Trial (+7 days)"}
              </Button>
            )}

            {/* 2. Activate (primary for at-risk, outline for already-dead) */}
            {!anyExpanded && canActivate && (
              <Button
                size="sm"
                variant={badgeStatus === "Cancelled" ? "outline" : "default"}
                className="h-7 text-xs gap-1"
                onClick={() => setActivating(true)}
                aria-label={`Activate subscription for ${studio.name}`}
              >
                <Banknote className="h-3.5 w-3.5" />
                Activate
              </Button>
            )}

            {/* 3. Suspend / Reactivate */}
            {confirmPlatform ? (
              <>
                <span className="text-xs text-muted-foreground">
                  {confirmPlatform === "suspend" ? "Suspend?" : "Reactivate?"}
                </span>
                <Button
                  size="sm"
                  variant={confirmPlatform === "suspend" ? "destructive" : "default"}
                  className="h-7 px-2 text-xs"
                  disabled={suspending || unsuspending}
                  onClick={executePlatform}
                >
                  {(suspending || unsuspending)
                    ? <Loader2 className="h-3 w-3 animate-spin" />
                    : "Yes"}
                </Button>
                <Button size="sm" variant="ghost" className="h-7 px-2 text-xs"
                  onClick={() => setConfirmPlatform(null)}>
                  No
                </Button>
              </>
            ) : (
              !anyExpanded && (
                <Button
                  size="sm" variant={isSuspended ? "default" : "ghost"} className="h-7 px-2 text-xs gap-1"
                  onClick={() => setConfirmPlatform(isSuspended ? "unsuspend" : "suspend")}
                >
                  {isSuspended
                    ? <><PlayCircle className="h-3.5 w-3.5" /> Reactivate</>
                    : <><PauseCircle className="h-3.5 w-3.5" /> Suspend</>}
                </Button>
              )
            )}

            {/* 4. Cancel Subscription (destructive outline — LAST) */}
            {!anyExpanded && canCancel && (
              <Button
                size="sm" variant="outline"
                className="h-7 text-xs gap-1 text-destructive border-destructive/40 hover:bg-destructive/10 hover:text-destructive"
                onClick={() => setConfirming(true)}>
                <XCircle className="h-3.5 w-3.5" />
                Cancel Subscription
              </Button>
            )}
          </div>
        </div>

        {/* ── Extend trial form ────────────────────────────────────────── */}
        {extending && (
          <div className="flex items-center gap-2 pt-1 border-t">
            <span className="text-xs text-muted-foreground">
              {trialExpired ? "Grant extension of" : "Extend trial by"}
            </span>
            <Input
              type="number" min="1" max="90"
              value={days} onChange={(e) => setDays(e.target.value)}
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

        {/* ── Activate (cash) form ─────────────────────────────────────── */}
        {activating && (
          <div className="pt-2 space-y-2 border-t">
            <p className="text-xs font-medium text-muted-foreground">Activate — Cash Payment</p>
            <div className="space-y-1">
              <Label htmlFor={`plan-${studio.id}`} className="text-xs">Plan</Label>
              <select
                id={`plan-${studio.id}`}
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
              <Label htmlFor={`note-${studio.id}`} className="text-xs">Note (optional)</Label>
              <Input
                id={`note-${studio.id}`}
                value={cashNote}
                onChange={(e) => setCashNote(e.target.value)}
                placeholder="e.g. Cash paid in person on 2026-06-13"
                className="h-8 text-xs"
              />
            </div>
            <div className="flex gap-2">
              <Button
                size="sm" className="h-7 px-2 text-xs flex-1"
                disabled={activating_ || !cashPlanId}
                onClick={handleActivate}
              >
                {activating_ ? <Loader2 className="h-3 w-3 animate-spin" /> : "Activate subscription"}
              </Button>
              <Button size="sm" variant="ghost" className="h-7 px-2 text-xs"
                onClick={() => { setActivating(false); setCashPlanId(""); setCashNote(""); }}>
                Cancel
              </Button>
            </div>
          </div>
        )}

        {/* ── Cancel subscription confirm ──────────────────────────────── */}
        {confirming && (
          <div className="flex items-center gap-2 pt-1 border-t">
            <span className="text-xs text-destructive font-medium">Cancel subscription permanently?</span>
            <Button
              size="sm" variant="destructive" className="h-7 px-2 text-xs"
              disabled={cancelling_} onClick={handleCancel}
            >
              {cancelling_ ? <Loader2 className="h-3 w-3 animate-spin" /> : "Confirm"}
            </Button>
            <Button size="sm" variant="ghost" className="h-7 px-2 text-xs"
              onClick={() => setConfirming(false)}>
              Back
            </Button>
          </div>
        )}

      </CardContent>
    </Card>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function IssuerStudioListPage() {
  useDocumentMeta({ title: "Studios — Platform Admin", canonical: "/platform/studios" });

  const location      = useLocation();
  const highlightId   = (location.state as { highlight?: string } | null)?.highlight ?? null;
  const [dimHighlight, setDimHighlight] = useState(false);
  const listRef       = useRef<HTMLDivElement>(null);

  const [search,       setSearch]       = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [planFilter,   setPlanFilter]   = useState("all");
  const [page,         setPage]         = useState(1);

  const { data: studios,       isLoading: studiosLoading, isError: studiosError } =
    useGetStudiosQuery();
  const { data: subscriptions, isLoading: subsLoading } =
    useGetPlatformSubscriptionsQuery(undefined, { refetchOnMountOrArgChange: true });
  const { data: plans = [] } = useGetIssuerPlansQuery();

  const subMap = useMemo(() => {
    const m = new Map<string, PlatformSubscriptionResponse>();
    subscriptions?.forEach((s) => m.set(s.studioId, s));
    return m;
  }, [subscriptions]);

  const filtered = useMemo(() => {
    if (!studios) return [];
    const q = search.trim().toLowerCase();
    return studios
      .filter((s) => {
        const sub             = subMap.get(s.id);
        const subStatus       = sub?.status ?? "NoSubscription";
        const effectiveStatus = !s.isActive ? "Suspended" : subStatus;

        const matchesSearch = !q ||
          s.name.toLowerCase().includes(q) ||
          s.slug.toLowerCase().includes(q);
        const matchesStatus =
          statusFilter === "all" || effectiveStatus === statusFilter;
        const matchesPlan = (() => {
          if (planFilter === "all") return true;
          if (planFilter === "none") return sub?.planName == null;
          return sub?.planName === planFilter;
        })();

        return matchesSearch && matchesStatus && matchesPlan;
      })
      .sort((a, b) => {
        const subA     = subMap.get(a.id);
        const subB     = subMap.get(b.id);
        const statusA  = !a.isActive ? "Suspended" : (subA?.status ?? "NoSubscription");
        const statusB  = !b.isActive ? "Suspended" : (subB?.status ?? "NoSubscription");
        const orderDiff = (STATUS_SORT_ORDER[statusA] ?? 9) - (STATUS_SORT_ORDER[statusB] ?? 9);
        return orderDiff !== 0 ? orderDiff : a.name.localeCompare(b.name);
      });
  }, [studios, subMap, search, statusFilter, planFilter]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));

  const pageStudios = useMemo(
    () => filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE),
    [filtered, page],
  );

  const groups = useMemo(() => {
    const result: Array<{ status: string; items: StudioResponse[] }> = [];
    for (const s of pageStudios) {
      const sub  = subMap.get(s.id);
      const eff  = !s.isActive ? "Suspended" : (sub?.status ?? "NoSubscription");
      const last = result.at(-1);
      if (last?.status === eff) {
        last.items.push(s);
      } else {
        result.push({ status: eff, items: [s] });
      }
    }
    return result;
  }, [pageStudios, subMap]);

  // Jump to whichever page contains the studio highlighted from the dashboard at-risk link.
  useEffect(() => {
    if (!highlightId) return;
    const idx = filtered.findIndex((s) => s.id === highlightId);
    if (idx === -1) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setPage(Math.floor(idx / PAGE_SIZE) + 1);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [highlightId, filtered.length]);

  // Clamp the current page if filtering shrinks the result set below it.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setPage((p) => Math.min(p, totalPages));
  }, [totalPages]);

  // Scroll to and highlight the studio arriving from the dashboard at-risk link.
  useEffect(() => {
    if (!highlightId || !listRef.current) return;
    const el = listRef.current.querySelector<HTMLElement>(`[data-studio-id="${highlightId}"]`);
    if (!el) return;
    el.scrollIntoView({ behavior: "smooth", block: "center" });
    setDimHighlight(false);
    const timer = setTimeout(() => setDimHighlight(true), 1800);
    return () => clearTimeout(timer);
  }, [highlightId, filtered.length, page]);

  const isLoading = studiosLoading || subsLoading;

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-[var(--issuer-nav-height)] z-10">
        <Building2 className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Studios</span>
        {studios && (
          <span className="ml-auto text-xs text-muted-foreground">
            {filtered.length === studios.length
              ? `${studios.length} studio${studios.length !== 1 ? "s" : ""}`
              : `${filtered.length} of ${studios.length}`}
          </span>
        )}
      </header>

      {/* ── Search + filter bar ──────────────────────────────────────── */}
      <div className="max-w-5xl mx-auto px-4 pt-4 flex gap-2 flex-wrap">
        <div className="relative flex-1">
          <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-muted-foreground pointer-events-none" />
          <Input
            aria-label="Search studios by name or slug"
            placeholder="Search by name or slug…"
            value={search}
            onChange={(e) => { setSearch(e.target.value); setPage(1); }}
            className="pl-8 h-8 text-sm"
          />
        </div>
        <select
          value={statusFilter}
          onChange={(e) => { setStatusFilter(e.target.value); setPage(1); }}
          aria-label="Filter by status"
          className="h-8 rounded-md border border-input bg-background px-2 text-xs"
        >
          <option value="all">All statuses</option>
          {ALL_FILTER_STATUSES.map((s) => (
            <option key={s} value={s}>{STATUS_LABELS[s]}</option>
          ))}
        </select>
        <select
          value={planFilter}
          onChange={(e) => { setPlanFilter(e.target.value); setPage(1); }}
          aria-label="Filter by plan"
          className="h-8 rounded-md border border-input bg-background px-2 text-xs"
        >
          <option value="all">All plans</option>
          {plans.map((p) => (
            <option key={p.id} value={p.name}>{p.name}</option>
          ))}
          <option value="none">No plan</option>
        </select>
      </div>

      <main className="max-w-5xl mx-auto px-4 py-4">
        {isLoading && (
          <div className="space-y-3">
            {[1, 2, 3, 4, 5].map((i) => <StudioRowSkeleton key={i} />)}
          </div>
        )}

        {studiosError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load studios.
          </p>
        )}

        {!isLoading && !studiosError && studios?.length === 0 && (
          <div className="flex flex-col items-center gap-4 py-20 text-center">
            <Building2 className="h-10 w-10 text-muted-foreground/50" />
            <div className="space-y-1">
              <p className="text-sm font-medium text-foreground">No studios registered yet</p>
              <p className="text-xs text-muted-foreground">
                Studios will appear here once they register on the platform.
              </p>
            </div>
          </div>
        )}

        {!isLoading && !studiosError && (studios?.length ?? 0) > 0 && filtered.length === 0 && (
          <p className="text-center text-sm text-muted-foreground py-16">
            No studios match your filters.
          </p>
        )}

        <div ref={listRef} className="space-y-4">
          {!isLoading && !studiosError && groups.map((group) => (
            <div key={group.status} className="space-y-3">
              {groups.length > 1 && (
                <div className="flex items-center gap-2 pt-1">
                  <span className={`text-xs px-1.5 py-0.5 rounded-full font-medium shrink-0 ${STATUS_CLASSES[group.status] ?? ""}`}>
                    {STATUS_LABELS[group.status] ?? group.status}
                  </span>
                  <span className="text-xs text-muted-foreground shrink-0">
                    {group.items.length} {group.items.length === 1 ? "studio" : "studios"}
                  </span>
                  <div className="flex-1 h-px bg-border" />
                </div>
              )}
              {group.items.map((s) => (
                <div
                  key={s.id}
                  data-studio-id={s.id}
                  className={`rounded-lg transition-shadow duration-700 ${
                    highlightId === s.id && !dimHighlight
                      ? "ring-2 ring-primary shadow-md"
                      : ""
                  }`}
                >
                  <StudioRow studio={s} sub={subMap.get(s.id)} plans={plans} />
                </div>
              ))}
            </div>
          ))}
        </div>

        {!isLoading && !studiosError && totalPages > 1 && (
          <div className="flex items-center justify-between pt-4">
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
