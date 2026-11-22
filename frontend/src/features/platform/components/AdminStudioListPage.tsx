import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
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
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  useGetStudiosQuery,
  useSuspendStudioMutation,
  useUnsuspendStudioMutation,
} from "@/features/studios/studiosApi";
import type { StudioResponse } from "@/features/studios/studiosApi";
import {
  useGetPlatformSubscriptionsQuery,
} from "@/features/platform/platformApi";
import type { PlatformSubscriptionResponse } from "@/features/platform/platform.types";
import { useGetAdminPlansQuery } from "@/features/billing/billingApi";
import type { PlanResponse } from "@/features/billing/billing.types";
import { ActivationReceiptNotice, CashActivationForm, type ActivationReceipt } from "./CashActivationForm";
import { CancelSubscriptionReview } from "./CancelSubscriptionReview";
import { ExtendTrialReview } from "./ExtendTrialReview";
import { CASH_ACTIVATABLE, TRIAL_EXTENDABLE, knowSubscriptionStatus, type SubscriptionKnowledge } from "./subscriptionKnowledge";

// ── Status display config ──────────────────────────────────────────────────────

const STATUS_CLASSES: Record<string, string> = {
  Active:         "bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-300",
  Trialing:       "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300",
  PastDue:        "bg-yellow-100 text-yellow-800 dark:bg-yellow-900/30 dark:text-yellow-300",
  GracePeriod:    "bg-orange-100 text-orange-700 dark:bg-orange-900/30 dark:text-orange-300",
  Cancelled:      "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300",
  NoSubscription: "bg-muted text-muted-foreground",
  Suspended:      "bg-amber-100 text-amber-800 dark:bg-amber-900/30 dark:text-amber-300",
  Unknown:        "border border-dashed text-muted-foreground",
};

const STATUS_LABELS: Record<string, string> = {
  Active:         "Active",
  Trialing:       "In Trial",
  PastDue:        "Past Due",
  GracePeriod:    "Grace Period",
  Cancelled:      "Cancelled",
  NoSubscription: "No Subscription",
  Suspended:      "Suspended",
  Unknown:        "Subscription unknown",
};
const CANCELLABLE      = new Set(["Active", "PastDue", "Trialing", "GracePeriod"]);

const STATUS_SORT_ORDER: Record<string, number> = {
  Suspended:      0,
  PastDue:        1,
  GracePeriod:    2,
  Trialing:       3,
  Active:         4,
  NoSubscription: 5,
  Cancelled:      6,
  Unknown:        7,
};

const ALL_FILTER_STATUSES = [
  "Active", "Trialing", "GracePeriod", "PastDue",
  "Cancelled", "NoSubscription", "Suspended",
] as const;

const PAGE_SIZE = 10;

// The at-risk and suspended views D08 asks to be reusable; each is just a status filter in the URL.
const QUICK_VIEWS: ReadonlyArray<{ status: (typeof ALL_FILTER_STATUSES)[number]; label: string }> = [
  { status: "PastDue",     label: "Past due" },
  { status: "GracePeriod", label: "Grace period" },
  { status: "Suspended",   label: "Suspended" },
];

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
        {/* Same stacked-on-phones / side-by-side-from-sm layout as the real row, so the skeleton
            doesn't push the page wider than the viewport while loading. */}
        <div className="flex flex-col gap-2 page-md:flex-row page-md:items-start page-md:justify-between page-md:gap-4">
          <div className="space-y-1.5 flex-1 min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <Skeleton className="h-4 w-32" />
              <Skeleton className="h-3 w-28" />
              <Skeleton className="h-5 w-16 rounded-full" />
            </div>
            <Skeleton className="h-3 w-64 max-w-full" />
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
  /** The list's own query with this studio as focus, so the detail page's Back link returns here (D08). */
  listSearch?: string;
  onOpen?: () => void;
  /** D02: whether the platform's subscriptions are known, loading or failed. Never "No subscription" when unknown. */
  knowledge: SubscriptionKnowledge;
  subsChecking: boolean;
  onRecheckSubs: () => void;
}

function StudioRow({ studio, sub, plans, listSearch, onOpen, knowledge, subsChecking, onRecheckSubs }: StudioRowProps) {
  const isSuspended = !studio.isActive;
  const subKnown    = knowledge.kind === "known";
  const subStatus   = knowledge.kind === "known" ? knowledge.status : "Unknown";
  const badgeStatus = isSuspended ? "Suspended" : subStatus;

  // Platform actions
  const [confirmPlatform, setConfirmPlatform] = useState<"suspend" | "unsuspend" | null>(null);
  const [suspend,   { isLoading: suspending   }] = useSuspendStudioMutation();
  const [unsuspend, { isLoading: unsuspending }] = useUnsuspendStudioMutation();

  // Subscription actions
  const [extending,  setExtending]  = useState(false);
  const [activating, setActivating] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [receipt,    setReceipt]    = useState<ActivationReceipt | null>(null);


  const canExtendTrial = subKnown && TRIAL_EXTENDABLE.has(subStatus);
  const canActivate    = subKnown && CASH_ACTIVATABLE.has(subStatus);
  const canCancel      = subKnown && CANCELLABLE.has(subStatus);
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

  const planDisplay = (() => {
    if (!subKnown) return "Plan unknown";
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
    const isTrialState = subKnown && (!sub || sub.status === "Trialing" || sub.status === "NoSubscription");
    if (isTrialState && trialDate) {
      return trialExpired ? `Trial expired: ${fmt(trialDate)}` : `Trial ends: ${fmt(trialDate)}`;
    }
    return null;
  })();

  return (
    <Card className={isSuspended ? "border-destructive/40" : ""}>
      <CardContent className="p-4 space-y-2">

        {/* ── Main row ─────────────────────────────────────────────────── */}
        {/* Stacked on phones, side by side from sm up (see SubscriptionOversightPage). The name / slug /
            status line wraps instead of forcing the page wider than the viewport. */}
        <div className="flex flex-col gap-2 page-md:flex-row page-md:items-start page-md:justify-between page-md:gap-4">
          <div className="space-y-0.5 min-w-0">
            <div className="flex items-center gap-x-2 gap-y-1 flex-wrap min-w-0">
              <span className="font-medium text-sm">{studio.name}</span>
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
                  className="inline-flex items-center ml-1 text-warning-text"
                >
                  <AlertTriangle className="h-3 w-3" />
                </span>
              )}
              {periodText && <>{" · "}{periodText}</>}
            </p>
          </div>

          {/* ── Action buttons ──────────────────────────────────────────── */}
          <div className="flex items-center gap-1.5 flex-wrap page-md:shrink-0 page-md:justify-end">

            {/* 0. View detail — always visible, never hidden by anyExpanded */}
            <Link to={`/platform/studios/${studio.id}`} state={{ listSearch }} onClick={onOpen}>
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
                className="h-7 text-xs gap-1 text-destructive-text border-destructive/40 hover:bg-destructive/10 hover:text-destructive-text"
                onClick={() => setConfirming(true)}>
                <XCircle className="h-3.5 w-3.5" />
                Cancel Subscription
              </Button>
            )}
          </div>
        </div>

        {/* ── Extend trial: the shared consequence review (D03) ───────────── */}
        {extending && (
          <ExtendTrialReview
            studioId={studio.id}
            studioName={studio.name}
            idSuffix={studio.id}
            status={subStatus}
            currentTrialEnd={trialDate ?? null}
            onDone={() => setExtending(false)}
            onBack={() => setExtending(false)}
          />
        )}

        {/* ── Activate (cash) form ─────────────────────────────────────── */}
        {activating && (
          <CashActivationForm
            studioId={studio.id}
            studioName={studio.name}
            plans={plans}
            idSuffix={studio.id}
            status={subStatus}
            notePlaceholder="e.g. Cash paid in person on 2026-06-13"
            knowledge={knowledge}
            checking={subsChecking}
            onRecheck={onRecheckSubs}
            onClose={() => setActivating(false)}
            onActivated={setReceipt}
          />
        )}
        {receipt && <ActivationReceiptNotice receipt={receipt} onDismiss={() => setReceipt(null)} />}

        {/* ── Cancel subscription: the shared consequence review (D03) ───── */}
        {confirming && (
          <CancelSubscriptionReview
            studioId={studio.id}
            studioName={studio.name}
            idSuffix={studio.id}
            onDone={() => setConfirming(false)}
            onBack={() => setConfirming(false)}
          />
        )}

      </CardContent>
    </Card>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function AdminStudioListPage() {
  useDocumentMeta({ title: "Studios — Platform Admin", canonical: "/platform/studios" });

  // Status, plan, page and the focused studio live in the URL (D08), so Back, a reload or a shared link
  // restores the view. The search box does NOT: an admin's search text can be a person's name, and nothing
  // private goes in a URL (the C08 rule), so it stays in memory and is lost on reload by design.
  const [params, setParams] = useSearchParams();
  const statusFilter  = params.get("status") ?? "all";
  const planFilter    = params.get("plan") ?? "all";
  const highlightId   = params.get("focus");
  const [dimHighlight, setDimHighlight] = useState(false);
  const listRef       = useRef<HTMLDivElement>(null);

  const [search, setSearchText] = useState("");

  // A filter change returns to page 1 and replaces the history entry; a page change pushes one.
  function setFilterParam(key: "status" | "plan", value: string) {
    setParams((prev) => {
      const next = new URLSearchParams(prev);
      if (value === "all") next.delete(key); else next.set(key, value);
      next.delete("page");
      next.delete("focus");
      return next;
    }, { replace: true });
  }

  function setSearch(value: string) {
    setSearchText(value);
    setParams((prev) => {
      const next = new URLSearchParams(prev);
      next.delete("page");
      return next;
    }, { replace: true });
  }

  function setPage(nextPage: number) {
    setParams((prev) => {
      const next = new URLSearchParams(prev);
      if (nextPage > 1) next.set("page", String(nextPage)); else next.delete("page");
      next.delete("focus");
      return next;
    });
  }

  // The list's query with a studio as focus: written onto this entry before opening the studio, so the
  // browser's Back lands on the same filters and page with that studio highlighted.
  function searchWithFocus(studioId: string): string {
    const next = new URLSearchParams(params);
    next.set("focus", studioId);
    if (!next.has("page") && page > 1) next.set("page", String(page));
    return `?${next.toString()}`;
  }

  function openStudio(studioId: string) {
    setParams(new URLSearchParams(searchWithFocus(studioId)), { replace: true });
  }

  const { data: studios,       isLoading: studiosLoading, isError: studiosError } =
    useGetStudiosQuery();
  const subsQuery = useGetPlatformSubscriptionsQuery(undefined, { refetchOnMountOrArgChange: true });
  const { data: subscriptions, isLoading: subsLoading } = subsQuery;
  const { data: plans = [] } = useGetAdminPlansQuery();

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
        // With subscriptions failed, every unsuspended studio is "Unknown", never "No subscription" (D02).
        const subStatus       = subsQuery.isError ? "Unknown" : (sub?.status ?? "NoSubscription");
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
        const statusA  = !a.isActive ? "Suspended" : (subsQuery.isError ? "Unknown" : (subA?.status ?? "NoSubscription"));
        const statusB  = !b.isActive ? "Suspended" : (subsQuery.isError ? "Unknown" : (subB?.status ?? "NoSubscription"));
        const orderDiff = (STATUS_SORT_ORDER[statusA] ?? 9) - (STATUS_SORT_ORDER[statusB] ?? 9);
        return orderDiff !== 0 ? orderDiff : a.name.localeCompare(b.name);
      });
  }, [studios, subMap, search, statusFilter, planFilter, subsQuery.isError]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));

  // The page is derived, not synced: an explicit ?page= wins; otherwise the page holding the focused studio;
  // and it is clamped when the filters shrink the list below it.
  const requestedPage = (() => {
    const fromUrl = Number.parseInt(params.get("page") ?? "", 10);
    if (fromUrl >= 1) return fromUrl;
    const focusIndex = highlightId ? filtered.findIndex((st) => st.id === highlightId) : -1;
    return focusIndex >= 0 ? Math.floor(focusIndex / PAGE_SIZE) + 1 : 1;
  })();
  const page = Math.min(requestedPage, totalPages);

  const pageStudios = useMemo(
    () => filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE),
    [filtered, page],
  );

  const groups = useMemo(() => {
    const result: Array<{ status: string; items: StudioResponse[] }> = [];
    for (const s of pageStudios) {
      const sub  = subMap.get(s.id);
      const eff  = !s.isActive ? "Suspended" : (subsQuery.isError ? "Unknown" : (sub?.status ?? "NoSubscription"));
      const last = result.at(-1);
      if (last?.status === eff) {
        last.items.push(s);
      } else {
        result.push({ status: eff, items: [s] });
      }
    }
    return result;
  }, [pageStudios, subMap, subsQuery.isError]);

  // Scroll to and highlight the focused studio (the dashboard's at-risk link, or Back from its detail page).
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
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-[var(--admin-nav-height)] z-10">
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
            onChange={(e) => setSearch(e.target.value)}
            className="pl-8 h-8 text-sm"
          />
        </div>
        <select
          value={statusFilter}
          onChange={(e) => setFilterParam("status", e.target.value)}
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
          onChange={(e) => setFilterParam("plan", e.target.value)}
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

      {/* Saved views: plain links, so each is bookmarkable and shareable (D08). */}
      <nav aria-label="Quick views" className="max-w-5xl mx-auto px-4 pt-2 flex flex-wrap items-center gap-2 text-xs">
        <span className="text-muted-foreground">Quick views:</span>
        {QUICK_VIEWS.map((v) => (
          <Link key={v.status} to={`?status=${v.status}`}
            aria-current={statusFilter === v.status ? "page" : undefined}
            className={`rounded-full border px-2 py-0.5 hover:bg-muted ${statusFilter === v.status ? "bg-muted font-medium" : ""}`}>
            {v.label}
          </Link>
        ))}
      </nav>

      <main className="max-w-5xl mx-auto px-4 py-4">
        {isLoading && (
          <div className="space-y-3">
            {[1, 2, 3, 4, 5].map((i) => <StudioRowSkeleton key={i} />)}
          </div>
        )}

        {studiosError && (
          <p className="text-center text-sm text-destructive-text py-16">
            Failed to load studios.
          </p>
        )}

        {!studiosError && subsQuery.isError && (
          <div role="alert" className="mb-3 flex flex-wrap items-center gap-2 rounded-md border border-dashed px-3 py-2 text-xs">
            <span>Subscription statuses couldn't be loaded, so each studio shows "Subscription unknown" and its subscription actions are hidden.</span>
            <Button size="sm" variant="outline" className="h-7 text-xs" onClick={() => void subsQuery.refetch()}>
              Retry
            </Button>
          </div>
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
                  <StudioRow studio={s} sub={subMap.get(s.id)} plans={plans}
                    listSearch={searchWithFocus(s.id)} onOpen={() => openStudio(s.id)}
                    knowledge={knowSubscriptionStatus(subsQuery, subMap.get(s.id))}
                    subsChecking={subsQuery.isFetching}
                    onRecheckSubs={() => void subsQuery.refetch()} />
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
      </main>
    </div>
  );
}
