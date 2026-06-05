import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { ArrowLeft, CheckCircle, Loader2, Zap } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { cn } from "@/shared/utils/cn";
import { useGetPlansQuery, useCreateSubscriptionMutation } from "../billingApi";
import type { PlanResponse } from "../billing.types";

function formatPrice(price: number): string {
  return new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", minimumFractionDigits: 0 }).format(price);
}

function PlanCard({
  plan,
  selected,
  onSelect,
  disabled,
}: {
  plan:     PlanResponse;
  selected: boolean;
  onSelect: () => void;
  disabled: boolean;
}) {
  const isYearly   = plan.billingInterval === "Yearly";
  const price      = isYearly ? plan.priceYearly : plan.priceMonthly;
  const perMonth   = isYearly ? plan.priceYearly / 12 : plan.priceMonthly;

  return (
    <button
      type="button"
      onClick={onSelect}
      disabled={disabled}
      className={cn(
        "w-full text-left rounded-lg border-2 p-4 transition-colors",
        selected
          ? "border-primary bg-primary/5"
          : "border-input hover:border-ring",
        disabled && "opacity-50 cursor-not-allowed",
      )}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="space-y-0.5">
          <p className="font-medium text-sm">{plan.name}</p>
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
  const navigate = useNavigate();
  const { data: plans = [], isLoading: loadingPlans, isError: plansError } = useGetPlansQuery();
  const [createSubscription, { isLoading: subscribing }] = useCreateSubscriptionMutation();

  const [selectedPlanId, setSelectedPlanId] = useState<string | null>(null);
  const [submitError,    setSubmitError]    = useState<string | null>(null);

  async function onSubscribe() {
    if (!selectedPlanId) return;
    setSubmitError(null);
    const result = await createSubscription({ planId: selectedPlanId });
    if ("error" in result) {
      setSubmitError("Failed to create subscription. Please try again.");
    } else {
      navigate("/billing");
    }
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-3 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/billing")}
          className="gap-1.5"
          disabled={subscribing}
        >
          <ArrowLeft className="h-4 w-4" />
          Billing
        </Button>
        <div className="flex items-center gap-2">
          <Zap className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Choose a Plan</span>
        </div>
      </header>

      <main className="max-w-lg mx-auto px-4 py-8 space-y-6">
        <p className="text-sm text-muted-foreground">
          Select a plan to unlock full access for your studio.
        </p>

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

        {plans.length > 0 && (
          <div className="space-y-3">
            {plans.map((plan) => (
              <PlanCard
                key={plan.id}
                plan={plan}
                selected={selectedPlanId === plan.id}
                onSelect={() => setSelectedPlanId(plan.id)}
                disabled={subscribing}
              />
            ))}
          </div>
        )}

        {submitError && (
          <p className="text-sm text-destructive">{submitError}</p>
        )}

        <Button
          className="w-full gap-1.5"
          disabled={!selectedPlanId || subscribing || loadingPlans}
          onClick={onSubscribe}
        >
          {subscribing ? (
            <>
              <Loader2 className="h-4 w-4 animate-spin" />
              Subscribing…
            </>
          ) : (
            <>
              <Zap className="h-4 w-4" />
              Subscribe
            </>
          )}
        </Button>
      </main>
    </div>
  );
}
