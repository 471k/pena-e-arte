import { useMemo, useState } from "react";
import {
  Banknote,
  Building2,
  Loader2,
  PauseCircle,
  PlayCircle,
  Search,
} from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
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

// ── Status display config ──────────────────────────────────────────────────────

const STATUS_CLASSES: Record<string, string> = {
  Active:         "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300",
  Trialing:       "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300",
  PastDue:        "bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-300",
  GracePeriod:    "bg-orange-100 text-orange-700 dark:bg-orange-900/30 dark:text-orange-300",
  Cancelled:      "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300",
  NoSubscription: "bg-muted text-muted-foreground",
  Suspended:      "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300",
};

const STATUS_LABELS: Record<string, string> = {
  Active:         "Active",
  Trialing:       "Trialing",
  PastDue:        "Past Due",
  GracePeriod:    "Grace Period",
  Cancelled:      "Cancelled",
  NoSubscription: "No Subscription",
  Suspended:      "Suspended",
};

const CASH_ACTIVATABLE = new Set(["NoSubscription", "PastDue", "GracePeriod", "Cancelled"]);
const CANCELLABLE      = new Set(["Active", "PastDue", "Trialing", "GracePeriod"]);

const ALL_FILTER_STATUSES = [
  "Active", "Trialing", "GracePeriod", "PastDue",
  "Cancelled", "NoSubscription", "Suspended",
] as const;

function fmt(date: string) {
  return new Date(date).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

// ── Row ───────────────────────────────────────────────────────────────────────

interface StudioRowProps {
  studio: StudioResponse;
  sub:    PlatformSubscriptionResponse | undefined;
}

function StudioRow({ studio, sub }: StudioRowProps) {
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
  const { data: plans = [] } = useGetIssuerPlansQuery();

  const canExtendTrial = subStatus !== "Active";
  const canActivate    = CASH_ACTIVATABLE.has(subStatus);
  const canCancel      = CANCELLABLE.has(subStatus);
  const anyExpanded    = extending || activating || confirming || confirmPlatform !== null;

  async function executePlatform() {
    try {
      if (confirmPlatform === "suspend")   await suspend(studio.id).unwrap();
      if (confirmPlatform === "unsuspend") await unsuspend(studio.id).unwrap();
    } catch {
      // mutation failed — optimistic update already rolled back by onQueryStarted
    } finally {
      setConfirmPlatform(null);
    }
  }

  async function handleExtend() {
    const d = parseInt(days, 10);
    if (isNaN(d) || d < 1) return;
    await extendTrial({ studioId: studio.id, additionalDays: d }).unwrap();
    setExtending(false);
  }

  async function handleActivate() {
    if (!cashPlanId) return;
    await activateManually({ studioId: studio.id, planId: cashPlanId, note: cashNote || undefined }).unwrap();
    setActivating(false);
    setCashPlanId("");
    setCashNote("");
  }

  async function handleCancel() {
    await cancelSub(studio.id).unwrap();
    setConfirming(false);
  }

  // Build the meta line
  const trialDate = sub?.trialExpiresAt ?? studio.trialExpiresAt;
  const trialExpired = new Date(trialDate) < new Date();
  const trialText = trialExpired
    ? "Trial expired"
    : `Trial expires ${fmt(trialDate)}`;
  const periodText = sub?.currentPeriodEnd && sub.status === "Active"
    ? `Period ends ${fmt(sub.currentPeriodEnd)}`
    : trialText;

  return (
    <Card className={isSuspended ? "border-destructive/40" : ""}>
      <CardContent className="p-4 space-y-2">

        {/* ── Main row ─────────────────────────────────────────────────── */}
        <div className="flex items-start justify-between gap-4">
          <div className="space-y-0.5 min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <span className="font-medium text-sm">{studio.name}</span>
              <span className="text-xs text-muted-foreground font-mono">{studio.slug}</span>
              <span className={`text-xs px-1.5 py-0.5 rounded-full font-medium ${STATUS_CLASSES[badgeStatus]}`}>
                {STATUS_LABELS[badgeStatus]}
              </span>
            </div>
            <p className="text-xs text-muted-foreground">
              {studio.city}
              {" · "}Registered {fmt(studio.createdAt)}
              {" · "}{sub?.planName ?? "No plan"}
              {" · "}{periodText}
            </p>
          </div>

          {/* ── Action buttons ──────────────────────────────────────────── */}
          <div className="flex items-center gap-1.5 shrink-0 flex-wrap justify-end">

            {/* Subscription actions — only when nothing else is expanded */}
            {!anyExpanded && canExtendTrial && (
              <Button size="sm" variant="outline" className="h-7 text-xs"
                onClick={() => setExtending(true)}>
                Extend trial
              </Button>
            )}
            {!anyExpanded && canActivate && (
              <Button size="sm" variant="outline" className="h-7 text-xs gap-1"
                onClick={() => setActivating(true)}>
                <Banknote className="h-3.5 w-3.5" />
                Activate
              </Button>
            )}
            {!anyExpanded && canCancel && (
              <Button
                size="sm" variant="outline"
                className="h-7 text-xs text-destructive border-destructive/40 hover:bg-destructive/10 hover:text-destructive"
                onClick={() => setConfirming(true)}>
                Cancel sub
              </Button>
            )}

            {/* Platform suspend/reactivate */}
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
                  size="sm" variant="ghost" className="h-7 px-2 text-xs gap-1"
                  onClick={() => setConfirmPlatform(isSuspended ? "unsuspend" : "suspend")}
                >
                  {isSuspended
                    ? <><PlayCircle className="h-3.5 w-3.5" /> Reactivate</>
                    : <><PauseCircle className="h-3.5 w-3.5" /> Suspend</>}
                </Button>
              )
            )}
          </div>
        </div>

        {/* ── Extend trial form ────────────────────────────────────────── */}
        {extending && (
          <div className="flex items-center gap-2 pt-1 border-t">
            <span className="text-xs text-muted-foreground">Extend trial by</span>
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
            <span className="text-xs text-destructive font-medium">Cancel this subscription?</span>
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
  const [search,       setSearch]       = useState("");
  const [statusFilter, setStatusFilter] = useState("all");

  const { data: studios,       isLoading: studiosLoading, isError: studiosError } =
    useGetStudiosQuery();
  const { data: subscriptions, isLoading: subsLoading } =
    useGetPlatformSubscriptionsQuery(undefined, { refetchOnMountOrArgChange: true });

  const subMap = useMemo(() => {
    const m = new Map<string, PlatformSubscriptionResponse>();
    subscriptions?.forEach((s) => m.set(s.studioId, s));
    return m;
  }, [subscriptions]);

  const filtered = useMemo(() => {
    if (!studios) return [];
    const q = search.trim().toLowerCase();
    return studios.filter((s) => {
      const sub           = subMap.get(s.id);
      const subStatus     = sub?.status ?? "NoSubscription";
      const effectiveStatus = !s.isActive ? "Suspended" : subStatus;

      const matchesSearch = !q ||
        s.name.toLowerCase().includes(q) ||
        s.slug.toLowerCase().includes(q);
      const matchesStatus =
        statusFilter === "all" || effectiveStatus === statusFilter;

      return matchesSearch && matchesStatus;
    });
  }, [studios, subMap, search, statusFilter]);

  const isLoading = studiosLoading || subsLoading;

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
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
      <div className="max-w-3xl mx-auto px-4 pt-4 flex gap-2">
        <div className="relative flex-1">
          <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-muted-foreground pointer-events-none" />
          <Input
            placeholder="Search by name or slug…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="pl-8 h-8 text-sm"
          />
        </div>
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value)}
          className="h-8 rounded-md border border-input bg-background px-2 text-xs"
        >
          <option value="all">All statuses</option>
          {ALL_FILTER_STATUSES.map((s) => (
            <option key={s} value={s}>{STATUS_LABELS[s]}</option>
          ))}
        </select>
      </div>

      <main className="max-w-3xl mx-auto px-4 py-4 space-y-3">
        {isLoading && (
          <div className="flex items-center justify-center py-16 gap-2 text-muted-foreground">
            <Loader2 className="h-5 w-5 animate-spin" />
            <span className="text-sm">Loading…</span>
          </div>
        )}

        {studiosError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load studios.
          </p>
        )}

        {!isLoading && !studiosError && filtered.length === 0 && (
          <p className="text-center text-sm text-muted-foreground py-16">
            {studios?.length === 0 ? "No studios yet." : "No studios match your filters."}
          </p>
        )}

        {!isLoading && !studiosError && filtered.map((s) => (
          <StudioRow key={s.id} studio={s} sub={subMap.get(s.id)} />
        ))}
      </main>
    </div>
  );
}
