import { useState, useMemo } from "react";
import { useParams, Link } from "react-router-dom";
import { toast } from "sonner";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import {
  ArrowLeft,
  Banknote,
  Building2,
  Clock,
  ExternalLink,
  Loader2,
  PauseCircle,
  PlayCircle,
  XCircle,
} from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  useGetStudioByIdQuery,
  useSuspendStudioMutation,
  useUnsuspendStudioMutation,
} from "@/features/studios/studiosApi";
import {
  useGetPlatformSubscriptionsQuery,
  useExtendTrialMutation,
  useActivateSubscriptionManuallyMutation,
  useCancelSubscriptionMutation,
  useGetIssuerStudioSummaryQuery,
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
  Suspended:      "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300",
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

function fmt(date: string | Date) {
  return new Date(date).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

export function IssuerStudioDetailPage() {
  const { studioId } = useParams<{ studioId: string }>();

  useDocumentMeta({ title: "Studio Details — Platform Admin", canonical: `/platform/studios/${studioId ?? ""}` });

  const { data: studio, isLoading: studioLoading, isError } =
    useGetStudioByIdQuery(studioId!, { skip: !studioId });
  const { data: subscriptions } =
    useGetPlatformSubscriptionsQuery(undefined, { refetchOnMountOrArgChange: true });
  const { data: plans = [] } = useGetIssuerPlansQuery();
  const { data: summary, isLoading: summaryLoading } =
    useGetIssuerStudioSummaryQuery(studioId!, { skip: !studioId });

  const sub: PlatformSubscriptionResponse | undefined = useMemo(
    () => subscriptions?.find((s) => s.studioId === studioId),
    [subscriptions, studioId],
  );

  const isSuspended = studio ? !studio.isActive : false;
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

  const trialDate    = sub?.trialExpiresAt ?? studio?.trialExpiresAt ?? "";
  const trialExpired = trialDate ? new Date(trialDate) < new Date() : false;

  async function executePlatform() {
    if (!studioId) return;
    try {
      if (confirmPlatform === "suspend")   await suspend(studioId).unwrap();
      if (confirmPlatform === "unsuspend") await unsuspend(studioId).unwrap();
      toast.success(confirmPlatform === "suspend" ? "Studio suspended" : "Studio reinstated");
    } catch {
      toast.error(confirmPlatform === "suspend" ? "Failed to suspend studio" : "Failed to reinstate studio");
    }
    finally { setConfirmPlatform(null); }
  }

  async function handleExtend() {
    if (!studioId) return;
    const d = parseInt(days, 10);
    if (isNaN(d) || d < 1) return;
    try {
      await extendTrial({ studioId, additionalDays: d }).unwrap();
      toast.success(`Trial extended by ${d} day${d !== 1 ? "s" : ""}`);
      setExtending(false);
    } catch {
      toast.error("Failed to extend trial");
    }
  }

  async function handleActivate() {
    if (!studioId || !cashPlanId) return;
    try {
      await activateManually({ studioId, planId: cashPlanId, note: cashNote || undefined }).unwrap();
      toast.success("Subscription activated");
    } catch {
      toast.error("Failed to activate subscription");
    }
    setActivating(false);
    setCashPlanId("");
    setCashNote("");
  }

  async function handleCancel() {
    if (!studioId) return;
    try {
      await cancelSub(studioId).unwrap();
      toast.success("Subscription cancelled");
      setConfirming(false);
    } catch {
      toast.error("Failed to cancel subscription");
    }
  }

  if (studioLoading) {
    return (
      <div className="min-h-screen bg-background">
        <div className="max-w-3xl mx-auto px-4 py-8 space-y-4">
          <Skeleton className="h-8 w-48" />
          <Skeleton className="h-40 w-full" />
          <Skeleton className="h-24 w-full" />
        </div>
      </div>
    );
  }

  if (isError || !studio) {
    return (
      <div className="min-h-screen bg-background">
        <div className="max-w-3xl mx-auto px-4 py-16 text-center">
          <p className="text-sm text-destructive">Studio not found.</p>
          <Link to="/platform/studios" className="text-sm text-primary hover:underline mt-2 inline-block">
            ← Back to Studios
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Link
          to="/platform/studios"
          className="flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground transition-colors"
        >
          <ArrowLeft className="h-4 w-4" />
          Studios
        </Link>
        <span className="text-muted-foreground">/</span>
        <Building2 className="h-4 w-4" />
        <span className="font-semibold tracking-tight text-sm">{studio.name}</span>
      </header>

      <main className="max-w-5xl mx-auto px-4 py-6">
        <div className="grid lg:grid-cols-[1fr_288px] gap-4 lg:gap-6 lg:items-start">

          {/* Left column */}
          <div className="space-y-4">

            {/* ── Studio Info Card ──────────────────────────────────────────── */}
            <Card>
              <CardHeader className="pb-2">
                <CardTitle className="text-sm flex items-center gap-2">
                  <span>{studio.name}</span>
                  <span
                    className={`text-xs px-1.5 py-0.5 rounded-full font-medium ${STATUS_CLASSES[badgeStatus]}`}
                  >
                    {STATUS_LABELS[badgeStatus]}
                  </span>
                </CardTitle>
              </CardHeader>
              <CardContent className="pt-0 space-y-1.5">
                {/* Fixed 3×2 grid — always rendered */}
                <div className="grid grid-cols-2 gap-x-6 gap-y-1.5 text-xs">
                  <div>
                    <span className="text-muted-foreground">Slug</span>
                    <p className="font-mono">{studio.slug}</p>
                  </div>
                  <div>
                    <span className="text-muted-foreground">City</span>
                    <p>{studio.city}</p>
                  </div>
                  <div>
                    <span className="text-muted-foreground">Registered</span>
                    <p>{fmt(studio.createdAt)}</p>
                  </div>
                  <div>
                    <span className="text-muted-foreground">Platform branding</span>
                    <p>{studio.showPlatformBranding ? "Shown" : "Hidden"}</p>
                  </div>
                  <div>
                    <span className="text-muted-foreground">Plan</span>
                    <p>
                      {subStatus === "Trialing"
                        ? "In Trial"
                        : subStatus === "NoSubscription"
                        ? "None"
                        : (sub?.planName ?? "—")}
                    </p>
                  </div>
                  <div>
                    <span className="text-muted-foreground">Subscription status</span>
                    <div className="mt-0.5">
                      <span className={`text-xs px-1.5 py-0.5 rounded-full font-medium ${STATUS_CLASSES[badgeStatus]}`}>
                        {STATUS_LABELS[badgeStatus]}
                      </span>
                    </div>
                  </div>
                </div>

                {/* Conditional fields — only if at least one is present */}
                {(trialDate || (sub?.currentPeriodEnd && sub.status === "Active")) && (
                  <div className="border-t pt-3 grid grid-cols-2 gap-x-6 gap-y-1.5 text-xs">
                    {sub?.currentPeriodEnd && sub.status === "Active" && (
                      <div>
                        <span className="text-muted-foreground">Renews</span>
                        <p>{fmt(sub.currentPeriodEnd)}</p>
                      </div>
                    )}
                    {trialDate && !(sub?.currentPeriodEnd && sub.status === "Active") && <div />}
                    {trialDate && (
                      <div>
                        <span className="text-muted-foreground">Trial expiry</span>
                        <p className="flex items-center gap-1.5 flex-wrap">
                          {fmt(trialDate)}
                          {trialExpired && (
                            <span className="inline-flex items-center px-1.5 py-0.5 rounded-full text-[10px] font-medium bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300">
                              Expired
                            </span>
                          )}
                        </p>
                      </div>
                    )}
                    {!trialDate && sub?.currentPeriodEnd && sub.status === "Active" && <div />}
                  </div>
                )}

                <div className="border-t pt-3">
                  <a
                    href={`/s/${studio.slug}`}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="inline-flex items-center gap-1.5 text-xs text-primary hover:underline"
                  >
                    <ExternalLink className="h-3.5 w-3.5" />
                    View public portfolio
                  </a>
                </div>
              </CardContent>
            </Card>

            {/* ── Studio Overview Card ─────────────────────────────────────────── */}
            <Card>
              <CardHeader className="pb-2">
                <CardTitle className="text-sm">Studio Overview</CardTitle>
              </CardHeader>
              <CardContent className="pt-0">
                {summaryLoading ? (
                  <div className="space-y-2">
                    <Skeleton className="h-4 w-full" />
                    <Skeleton className="h-4 w-2/3" />
                    <Skeleton className="h-4 w-1/2" />
                  </div>
                ) : summary ? (
                  <div className="space-y-3">
                    {/* Owner */}
                    <div className="text-xs space-y-0.5">
                      <p className="text-[10px] text-muted-foreground font-medium uppercase tracking-wider">
                        Owner
                      </p>
                      <p className="font-medium">{summary.ownerDisplayName}</p>
                      {summary.ownerEmail !== "—" && (
                        <a
                          href={`mailto:${summary.ownerEmail}`}
                          className="text-primary hover:underline"
                        >
                          {summary.ownerEmail}
                        </a>
                      )}
                      {summary.ownerEmail === "—" && (
                        <p className="text-muted-foreground">{summary.ownerEmail}</p>
                      )}
                    </div>

                    {/* Metrics */}
                    <div className="border-t pt-3 grid grid-cols-3 text-center gap-2">
                      <div>
                        <p className="text-base font-semibold tabular-nums">{summary.artistCount}</p>
                        <p className="text-[10px] text-muted-foreground mt-0.5">Artists</p>
                      </div>
                      <div>
                        <p className="text-base font-semibold tabular-nums">{summary.clientCount}</p>
                        <p className="text-[10px] text-muted-foreground mt-0.5">Clients</p>
                      </div>
                      <div>
                        <p className="text-base font-semibold tabular-nums">{summary.appointmentCount}</p>
                        <p className="text-[10px] text-muted-foreground mt-0.5">Appts</p>
                      </div>
                    </div>
                  </div>
                ) : (
                  <p className="text-xs text-muted-foreground">Summary unavailable.</p>
                )}
              </CardContent>
            </Card>

          </div>

          {/* Right column */}
          <div>
            {/* ── Actions Card ──────────────────────────────────────────────── */}
            <Card>
              <CardHeader className="pb-2">
                <CardTitle className="text-sm">Actions</CardTitle>
              </CardHeader>
              <CardContent className="pt-0 space-y-3">
                <div className="flex flex-wrap gap-2">
                  {/* 1. Extend trial */}
                  {!anyExpanded && canExtendTrial && (
                    <Button size="sm" variant="outline" className="h-9 text-xs gap-1"
                      onClick={() => setExtending(true)}>
                      <Clock className="h-3.5 w-3.5" />
                      {trialExpired ? "Grant extension" : "Extend Trial (+7 days)"}
                    </Button>
                  )}

                  {/* 2. Activate */}
                  {!anyExpanded && canActivate && (
                    <Button size="sm" className="h-9 text-xs gap-1"
                      onClick={() => setActivating(true)}>
                      <Banknote className="h-3.5 w-3.5" />
                      Activate
                    </Button>
                  )}

                  {/* 3. Suspend / Reactivate */}
                  {confirmPlatform ? (
                    <div className="flex flex-col gap-1.5 pt-2 border-t">
                      <p className="text-xs font-medium text-muted-foreground">
                        {confirmPlatform === "suspend" ? "Suspend this studio?" : "Reactivate this studio?"}
                      </p>
                      {confirmPlatform === "suspend" && (
                        <p className="text-xs text-muted-foreground">
                          This immediately hides the studio from Discover and blocks all owner and artist logins.
                        </p>
                      )}
                      <div className="flex items-center gap-2 mt-0.5">
                        <Button
                          size="sm"
                          variant={confirmPlatform === "suspend" ? "destructive" : "default"}
                          className="h-8 px-3 text-xs"
                          disabled={suspending || unsuspending}
                          onClick={executePlatform}
                        >
                          {(suspending || unsuspending)
                            ? <Loader2 className="h-3 w-3 animate-spin" />
                            : "Confirm"}
                        </Button>
                        <Button size="sm" variant="ghost" className="h-8 px-3 text-xs"
                          onClick={() => setConfirmPlatform(null)}>
                          Cancel
                        </Button>
                      </div>
                    </div>
                  ) : (
                    !anyExpanded && (
                      <Button
                        size="sm" variant="ghost" className="h-9 text-xs gap-1"
                        onClick={() => setConfirmPlatform(isSuspended ? "unsuspend" : "suspend")}
                      >
                        {isSuspended
                          ? <><PlayCircle className="h-3.5 w-3.5" /> Reactivate Studio</>
                          : <><PauseCircle className="h-3.5 w-3.5" /> Suspend Studio</>}
                      </Button>
                    )
                  )}

                  {/* 4. Cancel Subscription (LAST) */}
                  {!anyExpanded && canCancel && (
                    <Button
                      size="sm" variant="outline"
                      className="h-9 text-xs gap-1 text-destructive border-destructive/40 hover:bg-destructive/10 hover:text-destructive"
                      onClick={() => setConfirming(true)}>
                      <XCircle className="h-3.5 w-3.5" />
                      Cancel Subscription
                    </Button>
                  )}
                </div>

                {/* Extend trial form */}
                {extending && (
                  <div className="flex items-center gap-2 pt-2 border-t">
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
                      onClick={() => setExtending(false)}>Cancel</Button>
                  </div>
                )}

                {/* Activate (cash) form */}
                {activating && (
                  <div className="pt-2 space-y-2 border-t">
                    <p className="text-xs font-medium text-muted-foreground">Activate — Cash Payment</p>
                    <div className="space-y-1">
                      <Label htmlFor="detail-plan" className="text-xs">Plan</Label>
                      <select
                        id="detail-plan"
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
                      <Label htmlFor="detail-note" className="text-xs">Note (optional)</Label>
                      <Input
                        id="detail-note"
                        value={cashNote}
                        onChange={(e) => setCashNote(e.target.value)}
                        placeholder="e.g. Cash paid in person"
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

                {/* Cancel subscription confirm */}
                {confirming && (
                  <div className="flex flex-col gap-1.5 pt-2 border-t">
                    <p className="text-xs text-destructive font-medium">Cancel subscription permanently?</p>
                    <p className="text-xs text-muted-foreground">
                      Billing ends immediately. Studio data is retained and the studio can re-subscribe at any time.
                    </p>
                    <div className="flex items-center gap-2 mt-0.5">
                      <Button
                        size="sm" variant="destructive" className="h-7 px-2 text-xs"
                        disabled={cancelling_} onClick={handleCancel}
                      >
                        {cancelling_ ? <Loader2 className="h-3 w-3 animate-spin" /> : "Confirm"}
                      </Button>
                      <Button size="sm" variant="ghost" className="h-7 px-2 text-xs"
                        onClick={() => setConfirming(false)}>Back</Button>
                    </div>
                  </div>
                )}
              </CardContent>
            </Card>
          </div>

        </div>
      </main>
    </div>
  );
}
