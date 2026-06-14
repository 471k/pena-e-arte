import { useEffect, useRef } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { AlertTriangle, Banknote, Calendar, CalendarClock, CreditCard, Loader2, RefreshCw, ShieldX, Zap } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { cn } from "@/shared/utils/cn";
import {
  useGetSubscriptionQuery,
  useGetPlansQuery,
  useCancelPlanChangeMutation,
  useFinalizeCheckoutMutation,
} from "../billingApi";
import { useGetMyStudioQuery } from "@/features/studios/studiosApi";
import type { SubscriptionResponse, PlanResponse } from "../billing.types";

function daysUntil(iso: string): number {
  return Math.max(0, Math.ceil((new Date(iso).getTime() - Date.now()) / 86_400_000));
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
}

interface StatusConfig {
  label:   string;
  color:   string;
  icon:    React.ReactNode;
}

function statusConfig(status: SubscriptionResponse["status"]): StatusConfig {
  switch (status) {
    case "Trialing":    return { label: "Trial",         color: "text-blue-500",   icon: <Zap className="h-4 w-4" /> };
    case "Active":      return { label: "Active",        color: "text-green-500",  icon: <Zap className="h-4 w-4" /> };
    case "GracePeriod": return { label: "Grace Period",  color: "text-amber-500",  icon: <AlertTriangle className="h-4 w-4" /> };
    case "PastDue":     return { label: "Payment Failed", color: "text-red-500",   icon: <AlertTriangle className="h-4 w-4" /> };
    case "Cancelled":   return { label: "Cancelled",     color: "text-muted-foreground", icon: <RefreshCw className="h-4 w-4" /> };
  }
}

function planName(sub: SubscriptionResponse, plans: PlanResponse[]): string | null {
  if (!sub.planId) return null;
  return plans.find((p) => p.id === sub.planId)?.name ?? null;
}

function pendingPlanName(sub: SubscriptionResponse, plans: PlanResponse[]): string | null {
  if (!sub.pendingPlanId) return null;
  return plans.find((p) => p.id === sub.pendingPlanId)?.name ?? null;
}

export function BillingPage() {
  const navigate = useNavigate();
  // Always refetch on mount — subscription/plan can change out of band (webhooks,
  // issuer actions, a switch in another tab), and stale cache must not mislead the owner.
  const { data: sub,    isLoading: loadingSub,    isError: subError } =
    useGetSubscriptionQuery(undefined, { refetchOnMountOrArgChange: true });
  const { data: plans,  isLoading: loadingPlans } =
    useGetPlansQuery(undefined, { refetchOnMountOrArgChange: true });
  const { data: studio } =
    useGetMyStudioQuery(undefined, { refetchOnMountOrArgChange: true });
  const [cancelPlanChange, { isLoading: cancellingChange }] = useCancelPlanChangeMutation();

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

  if (loadingSub || loadingPlans) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center gap-2 text-muted-foreground">
        <Loader2 className="h-5 w-5 animate-spin" />
        <span className="text-sm">Loading…</span>
      </div>
    );
  }

  if (subError || !sub) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center">
        <p className="text-sm text-destructive">Failed to load subscription. Please try again.</p>
      </div>
    );
  }

  const cfg   = statusConfig(sub.status);
  const plan  = planName(sub, plans ?? []);
  const canSubscribe  = sub.status !== "Active";
  const isCashBilled  = sub.stripeSubscriptionId === null;
  const canChangePlan = sub.status === "Active" && !isCashBilled;

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <CreditCard className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Billing</span>
        </div>
        {canSubscribe && (
          <Button size="sm" onClick={() => navigate("/billing/subscribe")} className="gap-1.5">
            <Zap className="h-3.5 w-3.5" />
            {sub.status === "Trialing" || sub.status === "GracePeriod" ? "Subscribe" : "Reactivate"}
          </Button>
        )}
        {canChangePlan && (
          <Button size="sm" variant="outline" onClick={() => navigate("/billing/subscribe")} className="gap-1.5">
            <RefreshCw className="h-3.5 w-3.5" />
            Change plan
          </Button>
        )}
      </header>

      <main className="max-w-lg mx-auto px-4 py-8 space-y-4">

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
                    href={`mailto:${import.meta.env.VITE_CONTACT_EMAIL ?? "support@penaearte.com"}`}
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
          <CardContent className="p-5 space-y-3">
            <div className={cn("flex items-center gap-2 font-medium", cfg.color)}>
              {cfg.icon}
              <span>{cfg.label}</span>
            </div>

            {plan && (
              <p className="text-sm">
                Plan: <span className="font-medium">{plan}</span>
              </p>
            )}

            {sub.status === "Trialing" && (
              <div className="space-y-1">
                <p className="text-sm">
                  Trial ends <span className="font-medium">{formatDate(sub.trialExpiresAt)}</span>
                </p>
                <p className="text-xs text-muted-foreground">
                  {daysUntil(sub.trialExpiresAt)} day{daysUntil(sub.trialExpiresAt) !== 1 ? "s" : ""} remaining
                </p>
              </div>
            )}

            {sub.status === "Active" && (
              <div className="flex items-center gap-1.5 text-sm text-muted-foreground">
                <Calendar className="h-3.5 w-3.5 shrink-0" />
                {/* Cash subs don't auto-renew — the issuer re-activates each period */}
                <span>
                  {isCashBilled ? "Active until" : "Renews"} {formatDate(sub.currentPeriodEnd)}
                </span>
              </div>
            )}

            {sub.status === "GracePeriod" && (
              <div className="rounded-md border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-xs text-amber-600 dark:text-amber-400 space-y-0.5">
                <p className="font-medium">Trial expired — your studio is in read-only mode.</p>
                <p>Subscribe before {formatDate(sub.gracePeriodEnd)} to restore full access.</p>
                <p className="text-muted-foreground">
                  {daysUntil(sub.gracePeriodEnd)} day{daysUntil(sub.gracePeriodEnd) !== 1 ? "s" : ""} left.
                </p>
              </div>
            )}

            {sub.status === "PastDue" && (
              <div className="rounded-md border border-red-500/30 bg-red-500/10 px-3 py-2 text-xs text-red-600 dark:text-red-400">
                <p className="font-medium">Your last payment failed.</p>
                <p>Update your payment method to restore access.</p>
              </div>
            )}

            {sub.status === "Cancelled" && (
              <p className="text-sm text-muted-foreground">
                Your subscription has been cancelled. Reactivate to continue using the platform.
              </p>
            )}
          </CardContent>
        </Card>

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
                onClick={() => cancelPlanChange()}
              >
                {cancellingChange
                  ? <Loader2 className="h-4 w-4 animate-spin" />
                  : "Keep current plan"}
              </Button>
            </CardContent>
          </Card>
        )}

        {/* Cash-billed: keep cash (issuer-handled) or self-serve switch to card billing */}
        {sub.status === "Active" && isCashBilled && (
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
                  href={`mailto:${import.meta.env.VITE_CONTACT_EMAIL ?? "contact@penaearte.com"}`}
                  className="font-medium underline underline-offset-4"
                >
                  Contact us
                </a>
                .
              </p>
            </CardContent>
          </Card>
        )}

      </main>
    </div>
  );
}
