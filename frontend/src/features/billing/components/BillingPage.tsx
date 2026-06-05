import { useNavigate } from "react-router-dom";
import { AlertTriangle, Calendar, CheckCircle, CreditCard, Loader2, RefreshCw, Zap } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { cn } from "@/shared/utils/cn";
import { useGetSubscriptionQuery, useGetPlansQuery } from "../billingApi";
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

export function BillingPage() {
  const navigate = useNavigate();
  const { data: sub,   isLoading: loadingSub,   isError: subError }   = useGetSubscriptionQuery();
  const { data: plans, isLoading: loadingPlans } = useGetPlansQuery();

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
  const canSubscribe = sub.status !== "Active";

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
      </header>

      <main className="max-w-lg mx-auto px-4 py-8 space-y-4">

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
                <span>Renews {formatDate(sub.currentPeriodEnd)}</span>
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

        {/* Connect with Stripe */}
        <Card>
          <CardContent className="p-5 space-y-3">
            <div className="flex items-center justify-between">
              <p className="text-sm font-medium">Stripe Connect</p>
              {sub.isStripeConnected && (
                <span className="flex items-center gap-1 text-xs text-green-600 dark:text-green-400">
                  <CheckCircle className="h-3.5 w-3.5" />
                  Connected
                </span>
              )}
            </div>

            {sub.isStripeConnected ? (
              <>
                <p className="text-xs text-muted-foreground">
                  Your studio is connected to Stripe. Clients can pay deposits at booking.
                </p>
                <Button
                  variant="outline"
                  size="sm"
                  className="w-full gap-1.5"
                  onClick={() => navigate("/studio/connect")}
                >
                  <RefreshCw className="h-3.5 w-3.5" />
                  Update Stripe account
                </Button>
              </>
            ) : (
              <>
                <p className="text-xs text-muted-foreground">
                  Connect your studio to Stripe to accept deposit payments from clients.
                </p>
                <Button
                  variant="outline"
                  size="sm"
                  className="w-full gap-1.5"
                  onClick={() => navigate("/studio/connect")}
                >
                  <CreditCard className="h-3.5 w-3.5" />
                  Set up payments
                </Button>
              </>
            )}
          </CardContent>
        </Card>

      </main>
    </div>
  );
}
