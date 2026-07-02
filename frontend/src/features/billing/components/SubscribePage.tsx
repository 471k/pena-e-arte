import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { ArrowLeft, Banknote, CheckCircle, Loader2, RefreshCw, Zap } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { cn } from "@/shared/utils/cn";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import {
  useGetPlansQuery,
  useGetSubscriptionQuery,
  useCreateCheckoutMutation,
  useChangePlanMutation,
} from "../billingApi";
import type { PlanResponse } from "../billing.types";

function formatPrice(price: number): string {
  return new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", minimumFractionDigits: 0 }).format(price);
}

function PlanCard({
  plan,
  selected,
  onSelect,
  disabled,
  isCurrent = false,
}: {
  plan:       PlanResponse;
  selected:   boolean;
  onSelect:   () => void;
  disabled:   boolean;
  isCurrent?: boolean;
}) {
  const isYearly   = plan.billingInterval === "Yearly";
  const price      = isYearly ? plan.priceYearly : plan.priceMonthly;
  const perMonth   = isYearly ? plan.priceYearly / 12 : plan.priceMonthly;

  return (
    <button
      type="button"
      onClick={onSelect}
      disabled={disabled || isCurrent}
      className={cn(
        "w-full text-left rounded-lg border-2 p-4 transition-colors",
        selected
          ? "border-primary bg-primary/5"
          : "border-input hover:border-ring",
        (disabled || isCurrent) && "opacity-50 cursor-not-allowed",
      )}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="space-y-0.5">
          <div className="flex items-center gap-2">
            <p className="font-medium text-sm">{plan.name}</p>
            {isCurrent && (
              <span className="text-xs px-1.5 py-0.5 rounded-full bg-muted text-muted-foreground">
                Current plan
              </span>
            )}
          </div>
          <p className="text-xs text-muted-foreground">
            {plan.billingInterval === "Yearly" ? "Billed yearly" : "Billed monthly"}
          </p>
        </div>
        <div className="text-right shrink-0">
          <p className="font-semibold">{formatPrice(price)}<span className="text-xs font-normal text-muted-foreground">/{isYearly ? "yr" : "mo"}</span></p>
          {isYearly && (
            <p className="text-xs text-green-600 dark:text-green-400">
              {formatPrice(perMonth)}/mo · save {plan.yearlyDiscountPercent}%
            </p>
          )}
        </div>
      </div>
      {selected && (
        <div className="mt-2 flex items-center gap-1 text-xs text-primary">
          <CheckCircle className="h-3.5 w-3.5" />
          Selected
        </div>
      )}
    </button>
  );
}

export function SubscribePage() {
  useDocumentMeta({ title: "Subscribe — Pena e Artë", canonical: "/billing/subscribe" });

  const navigate = useNavigate();
  const { data: plans = [], isLoading: loadingPlans, isError: plansError } =
    useGetPlansQuery(undefined, { refetchOnMountOrArgChange: true });
  const { data: sub } = useGetSubscriptionQuery(undefined, { refetchOnMountOrArgChange: true });
  const [createCheckout,     { isLoading: checkingOut }] = useCreateCheckoutMutation();
  const [changePlan,         { isLoading: switching }]   = useChangePlanMutation();

  const [selectedPlanId, setSelectedPlanId] = useState<string | null>(null);
  const [submitError,    setSubmitError]    = useState<string | null>(null);
  const [billingCycle,   setBillingCycle]   = useState<"Monthly" | "Yearly">("Monthly");

  // Card-billed active studios change plans via Stripe (proration). Everyone else —
  // new subscribers AND cash-billed studios setting up card billing — goes via Checkout.
  const isActive          = sub?.status === "Active";
  const isCardBilled      = isActive && sub.stripeSubscriptionId !== null;
  const isCashBilled      = isActive && sub.stripeSubscriptionId === null;
  const hasPendingChange  = isCardBilled && sub.pendingPlanId !== null;
  const busy              = checkingOut || switching;

  // Derive once — used by the toggle label and the plan list.
  const yearlyDiscount = plans.find((p) => p.billingInterval === "Yearly")?.yearlyDiscountPercent ?? 0;
  const filteredPlans  = plans.filter((p) => p.billingInterval === billingCycle);

  function handleCycleChange(cycle: "Monthly" | "Yearly") {
    setBillingCycle(cycle);
    setSelectedPlanId(null);   // reset selection — different cycle = different plan IDs
  }

  async function onSubscribe() {
    if (!selectedPlanId) return;
    setSubmitError(null);

    if (isCardBilled) {
      const result = await changePlan({ planId: selectedPlanId });
      if ("error" in result) {
        const err = result.error as { data?: { message?: string } } | undefined;
        setSubmitError(err?.data?.message ?? "Failed to change plan. Please try again.");
        return;
      }
      // Backend decides: upgrade = immediate, downgrade = scheduled at period end
      if (result.data.pendingPlanId) {
        toast.success("Plan change scheduled for the end of your current billing period.");
      } else {
        toast.success("Plan upgraded — the prorated difference has been charged.");
      }
      navigate("/billing");
      return;
    }

    // New subscription OR cash → card switch → Stripe-hosted Checkout collects the card.
    const origin = window.location.origin;
    const result = await createCheckout({
      planId:     selectedPlanId,
      successUrl: `${origin}/billing?session_id={CHECKOUT_SESSION_ID}`,
      cancelUrl:  `${origin}/billing/subscribe`,
    });
    if ("error" in result) {
      const err = result.error as { data?: { message?: string } } | undefined;
      setSubmitError(err?.data?.message ?? "Could not start checkout. Please try again.");
      return;
    }
    window.location.href = result.data.url;
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-3 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/billing")}
          className="gap-1.5"
          disabled={busy}
        >
          <ArrowLeft className="h-4 w-4" />
          Billing
        </Button>
        <div className="flex items-center gap-2">
          <Zap className="h-5 w-5" />
          <span className="font-semibold tracking-tight">
            {isCardBilled ? "Change Plan" : isCashBilled ? "Set up card billing" : "Choose a Plan"}
          </span>
        </div>
      </header>

      <main className="max-w-lg mx-auto px-4 py-8 space-y-6">
        <p className="text-sm text-muted-foreground">
          {isCardBilled
            ? "Upgrades apply immediately (you pay only the prorated difference). Downgrades take effect at the end of your current billing period."
            : isCashBilled
            ? "Switch from cash to automatic card billing. Pick a plan and pay securely by card — you stay active throughout."
            : "Select a plan to unlock full access for your studio."}
        </p>

        {hasPendingChange && (
          <Card>
            <CardContent className="p-5 text-sm text-muted-foreground">
              A plan change is already scheduled. Cancel it from the Billing page before
              choosing another plan.
            </CardContent>
          </Card>
        )}

        {loadingPlans && (
          <div className="flex justify-center py-8">
            <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
          </div>
        )}

        {plansError && (
          <p className="text-sm text-destructive">Failed to load plans. Please try again.</p>
        )}

        {!loadingPlans && !plansError && plans.length === 0 && (
          <Card>
            <CardContent className="p-5 text-center text-sm text-muted-foreground">
              No plans available.
            </CardContent>
          </Card>
        )}

        {plans.length > 0 && !hasPendingChange && (
          <div className="space-y-3">
            {/* Monthly / Yearly billing cycle toggle */}
            <div
              role="group"
              aria-label="Billing cycle"
              className="flex items-center rounded-lg border border-input bg-muted p-1 text-sm font-medium"
            >
              {(["Monthly", "Yearly"] as const).map((cycle) => (
                <button
                  key={cycle}
                  type="button"
                  disabled={busy}
                  onClick={() => handleCycleChange(cycle)}
                  aria-pressed={billingCycle === cycle}
                  className={cn(
                    "flex flex-1 items-center justify-center gap-1.5 rounded-md px-4 py-1.5 transition-colors",
                    billingCycle === cycle
                      ? "bg-background shadow-sm text-foreground"
                      : "text-muted-foreground hover:text-foreground",
                  )}
                >
                  {cycle}
                  {cycle === "Yearly" && yearlyDiscount > 0 && (
                    <span className="rounded-full bg-green-500/15 px-1.5 py-0.5 text-xs font-normal text-green-600 dark:text-green-400">
                      Save {yearlyDiscount}%
                    </span>
                  )}
                </button>
              ))}
            </div>

            {filteredPlans.map((plan) => (
              <PlanCard
                key={plan.id}
                plan={plan}
                selected={selectedPlanId === plan.id}
                onSelect={() => setSelectedPlanId(plan.id)}
                disabled={busy}
                isCurrent={isCardBilled && plan.id === sub?.planId}
              />
            ))}

            {filteredPlans.length === 0 && (
              <Card>
                <CardContent className="p-5 text-center text-sm text-muted-foreground">
                  No {billingCycle.toLowerCase()} plans available.
                </CardContent>
              </Card>
            )}
          </div>
        )}

        {submitError && (
          <p className="text-sm text-destructive">{submitError}</p>
        )}

        {!hasPendingChange && (
          <Button
            className="w-full gap-1.5"
            disabled={!selectedPlanId || busy || loadingPlans}
            onClick={onSubscribe}
          >
            {busy ? (
              <>
                <Loader2 className="h-4 w-4 animate-spin" />
                {isCardBilled ? "Switching…" : "Redirecting to checkout…"}
              </>
            ) : isCardBilled ? (
              <>
                <RefreshCw className="h-4 w-4" />
                Switch plan
              </>
            ) : (
              <>
                <Zap className="h-4 w-4" />
                Continue to checkout
              </>
            )}
          </Button>
        )}

        {!isActive && (
        <div className="mt-6 rounded-lg border border-input p-4 space-y-2 text-sm">
          <p className="font-medium flex items-center gap-2">
            <Banknote className="h-4 w-4" />
            Prefer to pay cash?
          </p>
          <p className="text-muted-foreground">
            Contact us and we'll activate your subscription once payment is confirmed.
            Your trial continues until then.
          </p>
          <a
            href={`mailto:${import.meta.env.VITE_CONTACT_EMAIL ?? "contact@penaearte.com"}`}
            className="text-sm font-medium underline underline-offset-4"
          >
            Get in touch
          </a>
        </div>
        )}
      </main>
    </div>
  );
}
