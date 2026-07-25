import { useEffect, useMemo, useRef } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import {
  AlertTriangle, Banknote, Calendar, CalendarClock,
  CreditCard, ExternalLink, Loader2, RefreshCw, Settings, ShieldX, Zap,
} from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Badge } from "@/shared/components/ui/badge";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { cn } from "@/shared/utils/cn";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import {
  useGetSubscriptionQuery,
  useGetPlansQuery,
  useGetPlanUsageQuery,
  useCancelPlanChangeMutation,
  useFinalizeCheckoutMutation,
  useCreatePortalSessionMutation,
} from "../billingApi";
import { useGetMyStudioQuery } from "@/features/studios/studiosApi";
import { priceFor, type SubscriptionResponse, type PlanResponse, type PlanUsageDimension, type PlanUsageResponse } from "../billing.types";

function daysUntil(iso: string): number {
  return Math.max(0, Math.ceil((new Date(iso).getTime() - Date.now()) / 86_400_000));
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
}

function formatEur(euros: number): string {
  return new Intl.NumberFormat("pt-PT", {
    style:                 "currency",
    currency:              "EUR",
    minimumFractionDigits: 0,
  }).format(euros);
}

interface StatusConfig {
  label: string;
  color: string;
  icon:  React.ReactNode;
}

function statusConfig(status: SubscriptionResponse["status"]): StatusConfig {
  switch (status) {
    case "Trialing":    return { label: "Trial",          color: "text-blue-500",          icon: <Zap className="h-3.5 w-3.5" /> };
    case "Active":      return { label: "Active",         color: "text-green-500",         icon: <Zap className="h-3.5 w-3.5" /> };
    case "GracePeriod": return { label: "Grace Period",   color: "text-amber-500",         icon: <AlertTriangle className="h-3.5 w-3.5" /> };
    case "PastDue":     return { label: "Payment Failed", color: "text-red-500",           icon: <AlertTriangle className="h-3.5 w-3.5" /> };
    case "Cancelled":   return { label: "Cancelled",      color: "text-muted-foreground",  icon: <RefreshCw className="h-3.5 w-3.5" /> };
  }
}

function pendingPlanName(sub: SubscriptionResponse, plans: PlanResponse[]): string | null {
  if (!sub.pendingPlanId) return null;
  return plans.find((p) => p.id === sub.pendingPlanId)?.name ?? null;
}

interface UsageRowConfig {
  label: string;
  dim:   PlanUsageDimension;
  unit?: string;
}

function formatUsageValue(value: number): string {
  return Number.isInteger(value) ? String(value) : value.toFixed(1);
}

function UsageRow({ label, dim, unit }: UsageRowConfig) {
  const isUnlimited = dim.max === null;
  const pct = isUnlimited ? 0 : Math.min(100, (dim.current / Math.max(dim.max!, 1)) * 100);
  const isNearCap = !isUnlimited && pct >= 80;

  return (
    <div className="space-y-1">
      <div className="flex items-center justify-between text-xs">
        <span className="text-muted-foreground">{label}</span>
        <span className={cn("font-medium tabular-nums", isNearCap && "text-amber-600 dark:text-amber-400")}>
          {isUnlimited
            ? `${formatUsageValue(dim.current)}${unit ?? ""} · Unlimited`
            : `${formatUsageValue(dim.current)} / ${dim.max}${unit ?? ""}`}
        </span>
      </div>
      {!isUnlimited && (
        <div className="h-1.5 w-full rounded-full bg-muted overflow-hidden">
          <div
            className={cn("h-full rounded-full", isNearCap ? "bg-amber-500" : "bg-primary")}
            style={{ width: `${pct}%` }}
          />
        </div>
      )}
    </div>
  );
}

function PlanUsagePanel({ usage }: { usage: PlanUsageResponse }) {
  return (
    <Card>
      <CardContent className="p-5 space-y-4">
        <p className="text-sm font-medium">Plan usage</p>
        <div className="space-y-3">
          <UsageRow label="Artists"                    dim={usage.artists} />
          <UsageRow label="Appointments this month"     dim={usage.appointmentsPerMonth} />
          <UsageRow label="Notifications this month"    dim={usage.notificationsPerMonth} />
          <UsageRow label="Storage"                     dim={usage.storageGb} unit=" GB" />
          <UsageRow label="Locations"                   dim={usage.locations} />
        </div>
      </CardContent>
    </Card>
  );
}

function BillingPageSkeleton() {
  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-[var(--issuer-nav-height)] z-10">
        <div className="flex items-center gap-2">
          <CreditCard className="h-5 w-5 text-muted-foreground" />
          <Skeleton className="h-5 w-16" />
        </div>
      </header>
      <main className="max-w-2xl mx-auto px-4 py-8 space-y-4" aria-label="Loading billing information">
        <div className="rounded-xl border bg-card p-5 space-y-3">
          <Skeleton className="h-5 w-20 rounded-full" />
          <Skeleton className="h-4 w-32" />
          <Skeleton className="h-4 w-44" />
          <div className="flex gap-2 pt-1">
            <Skeleton className="h-8 w-28 rounded-md" />
            <Skeleton className="h-8 w-32 rounded-md" />
          </div>
        </div>
      </main>
    </div>
  );
}

export function BillingPage() {
  useDocumentMeta({ title: "Billing — TattooOS", canonical: "/billing" });

  const navigate = useNavigate();
  // Always refetch on mount — subscription/plan can change out of band (webhooks,
  // issuer actions, a switch in another tab), and stale cache must not mislead the owner.
  const { data: sub,   isLoading: loadingSub,   isError: subError } =
    useGetSubscriptionQuery(undefined, { refetchOnMountOrArgChange: true });
  const { data: plans, isLoading: loadingPlans } =
    useGetPlansQuery(undefined, { refetchOnMountOrArgChange: true });
  const { data: studio } =
    useGetMyStudioQuery(undefined, { refetchOnMountOrArgChange: true });
  const { data: usage } =
    useGetPlanUsageQuery(undefined, { refetchOnMountOrArgChange: true });
  const [cancelPlanChange, { isLoading: cancellingChange }] = useCancelPlanChangeMutation();

  async function handleCancelPlanChange() {
    try {
      await cancelPlanChange().unwrap();
      toast.success("Scheduled plan change cancelled.");
    } catch {
      toast.error("Failed to cancel the scheduled plan change.");
    }
  }
  const [createPortalSession, { isLoading: openingPortal }] = useCreatePortalSessionMutation();

  // Resolve the full PlanResponse so we can show price information.
  // Must be before early returns to satisfy Rules of Hooks.
  const currentPlan = useMemo<PlanResponse | null>(
    () => (sub?.planId && plans ? (plans.find((p) => p.id === sub.planId) ?? null) : null),
    [sub, plans],
  );

  // Returning from Stripe Checkout: reconcile the session (covers a missed webhook),
  // then strip session_id from the URL. The Subscription query is invalidated, so the
  // page refreshes to Active automatically.
  const [searchParams, setSearchParams] = useSearchParams();
  const [finalizeCheckout] = useFinalizeCheckoutMutation();
  const finalizedRef = useRef(false);

  useEffect(() => {
    const sessionId = searchParams.get("session_id");
    if (!sessionId || finalizedRef.current) return;
    finalizedRef.current = true;

    void (async () => {
      const result = await finalizeCheckout({ sessionId });
      setSearchParams({}, { replace: true });
      if ("data" in result && result.data) {
        toast.success("Subscription active — welcome aboard!");
      } else {
        toast("Finalizing your subscription… this can take a moment.");
      }
    })();
  }, [searchParams, finalizeCheckout, setSearchParams]);

  async function handleManageBilling() {
    const returnUrl = window.location.href;
    const result = await createPortalSession({ returnUrl });
    if ("data" in result && result.data?.url) {
      window.location.href = result.data.url;
    }
  }

  if (loadingSub || loadingPlans) {
    return <BillingPageSkeleton />;
  }

  if (subError || !sub) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center">
        <p className="text-sm text-destructive">Failed to load subscription. Please try again.</p>
      </div>
    );
  }

  const cfg           = statusConfig(sub.status);
  const isCashBilled  = sub.stripeSubscriptionId === null;
  const currentPrice  = currentPlan ? priceFor(currentPlan, sub.billingInterval) : undefined;
  const isFreePlan    = (currentPrice?.price ?? -1) === 0;
  // Free-plan studios are Active + cash-billed by the existing model, but still need a
  // way to move to a paid plan — canSubscribe therefore also covers "Active on Free".
  const canSubscribe  = sub.status !== "Active" || isFreePlan;
  const canChangePlan = sub.status === "Active" && !isCashBilled;

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-[var(--issuer-nav-height)] z-10">
        <div className="flex items-center gap-2">
          <CreditCard className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Billing</span>
        </div>
        {canSubscribe && (
          <Button size="sm" onClick={() => navigate("/billing/subscribe")} className="gap-1.5">
            <Zap className="h-3.5 w-3.5" />
            {isFreePlan && sub.status === "Active"
              ? "Upgrade"
              : sub.status === "Trialing" || sub.status === "GracePeriod"
                ? "Subscribe"
                : "Reactivate"}
          </Button>
        )}
      </header>

      <main className="max-w-2xl mx-auto px-4 py-8 space-y-4">

        {/* Studio suspended — shown when issuer has suspended the studio independently of billing */}
        {studio && !studio.isActive && (
          <Card className="border-red-500/50">
            <CardContent className="p-4 flex items-start gap-3 bg-red-500/10 rounded-lg">
              <ShieldX className="h-5 w-5 text-red-500 shrink-0 mt-0.5" />
              <div className="space-y-1">
                <p className="text-sm font-medium text-red-700 dark:text-red-400">
                  Studio suspended
                </p>
                <p className="text-xs text-red-600/80 dark:text-red-400/80">
                  Your studio has been suspended by the platform administrator. Your billing
                  subscription below remains active, but your studio is not accessible to
                  clients until the suspension is lifted. Contact{" "}
                  <a
                    href={`mailto:${import.meta.env.VITE_CONTACT_EMAIL ?? "support@tattooos.co"}`}
                    className="font-medium underline underline-offset-4"
                  >
                    support
                  </a>{" "}
                  to resolve this.
                </p>
              </div>
            </CardContent>
          </Card>
        )}

        {/* Status card */}
        <Card>
          <CardContent className="p-5 space-y-4">

            {/* Row 1: Plan badge + Status badge */}
            <div className="flex items-center gap-2 flex-wrap">
              {currentPlan ? (
                <Badge variant="outline" className="text-sm font-medium px-2.5 py-0.5">
                  {currentPlan.name}
                </Badge>
              ) : (
                <span className="text-sm text-muted-foreground">No plan selected</span>
              )}
              <span
                className={cn(
                  "inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-xs font-medium",
                  sub.status === "Active"      && "border-green-500/20 bg-green-500/10 text-green-600 dark:text-green-400",
                  sub.status === "Trialing"    && "border-blue-500/20 bg-blue-500/10 text-blue-600 dark:text-blue-400",
                  sub.status === "GracePeriod" && "border-amber-500/20 bg-amber-500/10 text-amber-600 dark:text-amber-400",
                  sub.status === "PastDue"     && "border-red-500/20 bg-red-500/10 text-red-600 dark:text-red-400",
                  sub.status === "Cancelled"   && "border-border bg-muted text-muted-foreground",
                )}
              >
                {cfg.icon}
                {cfg.label}
              </span>
            </div>

            {/* Row 2: Price + renewal date (Active states only) */}
            {sub.status === "Active" && (
              <div className="space-y-1">
                {currentPlan && (
                  isFreePlan ? (
                    <p className="text-sm font-medium text-green-600 dark:text-green-400">Free</p>
                  ) : (
                    <p className="text-sm font-medium">
                      {formatEur(currentPrice?.price ?? 0)}
                      <span className="text-muted-foreground font-normal"> / {sub.billingInterval === "Yearly" ? "year" : "month"}</span>
                    </p>
                  )
                )}
                {/* Free plan's period end is a far-future sentinel — showing it would confuse users */}
                {!isFreePlan && (
                  <div className="flex items-center gap-1.5 text-sm text-muted-foreground">
                    <Calendar className="h-3.5 w-3.5 shrink-0" />
                    {sub.cancelAtPeriodEnd
                      ? <span className="text-amber-600 dark:text-amber-400">Cancels on {formatDate(sub.currentPeriodEnd)}</span>
                      : isCashBilled
                        ? <span>Active until {formatDate(sub.currentPeriodEnd)}</span>
                        : currentPlan
                          ? <span>Next charge: {formatEur(currentPrice?.price ?? 0)} on {formatDate(sub.currentPeriodEnd)}</span>
                          : <span>Renews {formatDate(sub.currentPeriodEnd)}</span>
                    }
                  </div>
                )}
              </div>
            )}

            {/* Trial remaining (Trialing) */}
            {sub.status === "Trialing" && (
              <div className="space-y-1">
                <p className="text-sm">
                  Trial ends <span className="font-medium">{formatDate(sub.trialExpiresAt ?? sub.currentPeriodEnd)}</span>
                </p>
                <p className="text-xs text-muted-foreground">
                  {daysUntil(sub.trialExpiresAt ?? sub.currentPeriodEnd)} day{daysUntil(sub.trialExpiresAt ?? sub.currentPeriodEnd) !== 1 ? "s" : ""} remaining
                </p>
              </div>
            )}

            {/* Grace period warning (GracePeriod) */}
            {sub.status === "GracePeriod" && (
              <div className="rounded-md border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-xs text-amber-600 dark:text-amber-400 space-y-0.5">
                <p className="font-medium">Trial expired — your studio is in read-only mode.</p>
                <p>Subscribe before {formatDate(sub.gracePeriodEnd)} to restore full access.</p>
                <p className="text-muted-foreground">
                  {daysUntil(sub.gracePeriodEnd)} day{daysUntil(sub.gracePeriodEnd) !== 1 ? "s" : ""} left.
                </p>
              </div>
            )}

            {/* Payment failed warning (PastDue) */}
            {sub.status === "PastDue" && (
              <div className="rounded-md border border-red-500/30 bg-red-500/10 px-3 py-2 text-xs text-red-600 dark:text-red-400">
                <p className="font-medium">Your last payment failed.</p>
                <p>Update your payment method to restore access.</p>
              </div>
            )}

            {/* Cancelled */}
            {sub.status === "Cancelled" && (
              <p className="text-sm text-muted-foreground">
                Your subscription has been cancelled. Reactivate to continue using the platform.
              </p>
            )}

            {/* Actions — Change plan (primary) + Manage billing (secondary) for Active card-billed */}
            {canChangePlan && (
              <div className="flex items-center gap-2 pt-1 flex-wrap">
                <Button
                  size="sm"
                  className="gap-1.5"
                  onClick={() => navigate("/billing/subscribe")}
                >
                  <RefreshCw className="h-3.5 w-3.5" />
                  Change plan
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  className="gap-1.5"
                  disabled={openingPortal}
                  onClick={() => void handleManageBilling()}
                >
                  {openingPortal
                    ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
                    : <Settings className="h-3.5 w-3.5" />
                  }
                  Manage billing
                  {!openingPortal && <ExternalLink className="h-3 w-3 opacity-40" />}
                </Button>
              </div>
            )}
          </CardContent>
        </Card>

        {/* Subscription scheduled to cancel at period end (set via the Stripe billing
            portal — there is no in-app "undo", so point back to the same portal). */}
        {sub.status === "Active" && sub.cancelAtPeriodEnd && (
          <Card className="border-amber-500/20">
            <CardContent className="p-5 space-y-3">
              <p className="text-sm font-medium flex items-center gap-2 text-amber-600 dark:text-amber-400">
                <AlertTriangle className="h-4 w-4" />
                Subscription ending
              </p>
              <p className="text-sm text-muted-foreground">
                Your subscription is set to cancel on{" "}
                <span className="font-medium text-foreground">{formatDate(sub.currentPeriodEnd)}</span>.
                You keep access until then.
              </p>
              <Button
                variant="outline"
                size="sm"
                className="w-full gap-1.5"
                disabled={openingPortal}
                onClick={() => void handleManageBilling()}
              >
                {openingPortal
                  ? <Loader2 className="h-4 w-4 animate-spin" />
                  : <Settings className="h-3.5 w-3.5" />
                }
                Manage billing
                {!openingPortal && <ExternalLink className="h-3 w-3 opacity-40" />}
              </Button>
            </CardContent>
          </Card>
        )}

        {/* Plan usage — current vs. cap across all five dimensions */}
        {usage && <PlanUsagePanel usage={usage} />}

        {/* Scheduled plan change (downgrade at period end) */}
        {sub.pendingPlanId && (
          <Card>
            <CardContent className="p-5 space-y-3">
              <p className="text-sm font-medium flex items-center gap-2">
                <CalendarClock className="h-4 w-4" />
                Scheduled plan change
              </p>
              <p className="text-sm text-muted-foreground">
                Your plan changes to{" "}
                <span className="font-medium text-foreground">
                  {pendingPlanName(sub, plans ?? []) ?? "another plan"}
                </span>{" "}
                on {formatDate(sub.currentPeriodEnd)}. You keep your current plan until then.
              </p>
              <Button
                variant="outline"
                size="sm"
                className="w-full"
                disabled={cancellingChange}
                onClick={handleCancelPlanChange}
              >
                {cancellingChange
                  ? <Loader2 className="h-4 w-4 animate-spin" />
                  : "Keep current plan"}
              </Button>
            </CardContent>
          </Card>
        )}

        {/* Cash-billed: keep cash (issuer-handled) or self-serve switch to card billing */}
        {sub.status === "Active" && isCashBilled && !isFreePlan && (
          <Card>
            <CardContent className="p-5 space-y-3 text-sm">
              <p className="font-medium flex items-center gap-2">
                <Banknote className="h-4 w-4" />
                Cash-billed subscription
              </p>
              <p className="text-muted-foreground">
                Your subscription is settled in cash. Want to pay by card instead and manage
                your plan yourself? Switch to card billing below.
              </p>
              <Button
                size="sm"
                className="w-full gap-1.5"
                onClick={() => navigate("/billing/subscribe")}
              >
                <CreditCard className="h-3.5 w-3.5" />
                Switch to card billing
              </Button>
              <p className="text-xs text-muted-foreground">
                Prefer to keep paying cash?{" "}
                <a
                  href={`mailto:${import.meta.env.VITE_CONTACT_EMAIL ?? "contact@tattooos.co"}`}
                  className="font-medium underline underline-offset-4"
                >
                  Contact us
                </a>
                .
              </p>
            </CardContent>
          </Card>
        )}

        {/* Free plan: informational card with an upgrade path */}
        {sub.status === "Active" && isFreePlan && (
          <Card>
            <CardContent className="p-5 space-y-3 text-sm">
              <p className="font-medium flex items-center gap-2">
                <Zap className="h-4 w-4" />
                Free plan
              </p>
              <p className="text-muted-foreground">
                You're on the permanent Free plan. Upgrade to a paid plan to unlock more
                artists, appointments, storage, and features.
              </p>
              <Button
                size="sm"
                className="w-full gap-1.5"
                onClick={() => navigate("/billing/subscribe")}
              >
                <Zap className="h-3.5 w-3.5" />
                Upgrade plan
              </Button>
            </CardContent>
          </Card>
        )}

      </main>
    </div>
  );
}
