import { useState } from "react";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { CreditCard, Edit2, Loader2, Plus, Trash2, Users, X } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { cn } from "@/shared/utils/cn";
import {
  useGetIssuerPlansQuery,
  useCreatePlanMutation,
  useUpdatePlanMutation,
  useDeletePlanMutation,
} from "@/features/billing/billingApi";
import type { PlanResponse } from "@/features/billing/billing.types";

const schema = z.object({
  name:                  z.string().min(1, "Name is required").max(100),
  billingInterval:       z.enum(["Monthly", "Yearly"]),
  priceMonthly:          z.number({ message: "Required" }).positive(),
  priceYearly:           z.number({ message: "Required" }).positive(),
  yearlyDiscountPercent: z.number({ message: "Required" }).min(0).max(100),
  allowBrandingRemoval:  z.boolean(),
  stripePriceIdMonthly:  z.string().max(200).optional().nullable(),
  stripePriceIdYearly:   z.string().max(200).optional().nullable(),
});

type FormValues = z.infer<typeof schema>;

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-GB", {
    style:                 "currency",
    currency:              "EUR",
    minimumFractionDigits: 0,
    maximumFractionDigits: 2,
  }).format(amount);
}

interface PlanFormProps {
  defaultValues?: Partial<FormValues>;
  onSave:  (v: FormValues) => Promise<void>;
  onClose: () => void;
  saving:  boolean;
}

function PlanForm({ defaultValues, onSave, onClose, saving }: PlanFormProps) {
  const { register, handleSubmit, watch, formState: { errors } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { billingInterval: "Monthly", yearlyDiscountPercent: 17, allowBrandingRemoval: false, ...defaultValues },
  });

  const watchedMonthly  = watch("priceMonthly");
  const watchedDiscount = watch("yearlyDiscountPercent");
  const suggestedYearly =
    watchedMonthly > 0 && watchedDiscount >= 0 && watchedDiscount < 100
      ? watchedMonthly * 12 * (1 - watchedDiscount / 100)
      : null;

  const selectClass = cn(
    "flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm",
    "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
    "disabled:cursor-not-allowed disabled:opacity-50"
  );

  return (
    <form onSubmit={handleSubmit(onSave)} className="space-y-3 p-4 border rounded-lg bg-background">
      <div className="space-y-1.5">
        <Label htmlFor="planName">Name</Label>
        <Input id="planName" {...register("name")} aria-invalid={!!errors.name} />
        {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="billingInterval">Billing interval</Label>
        <select id="billingInterval" {...register("billingInterval")} className={selectClass}>
          <option value="Monthly">Monthly</option>
          <option value="Yearly">Yearly</option>
        </select>
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label htmlFor="priceMonthly">Monthly price (€)</Label>
          <Input id="priceMonthly" type="number" step="0.01" min="0"
            {...register("priceMonthly", { valueAsNumber: true })} />
          {errors.priceMonthly && <p className="text-xs text-destructive">{errors.priceMonthly.message}</p>}
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="priceYearly">Yearly price (€)</Label>
          <Input id="priceYearly" type="number" step="0.01" min="0"
            {...register("priceYearly", { valueAsNumber: true })} />
          {suggestedYearly !== null && (
            <p className="text-[10px] text-muted-foreground">
              Suggested: {formatCurrency(suggestedYearly)} (monthly × 12 × {100 - watchedDiscount}%)
            </p>
          )}
          {errors.priceYearly && <p className="text-xs text-destructive">{errors.priceYearly.message}</p>}
        </div>
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="discount">Yearly discount (%)</Label>
        <Input id="discount" type="number" min="0" max="100"
          {...register("yearlyDiscountPercent", { valueAsNumber: true })} />
        {errors.yearlyDiscountPercent && (
          <p className="text-xs text-destructive">{errors.yearlyDiscountPercent.message}</p>
        )}
      </div>

      <div className="flex items-center gap-2">
        <input
          id="allowBrandingRemoval"
          type="checkbox"
          className="h-4 w-4 rounded border-input accent-primary"
          {...register("allowBrandingRemoval")}
        />
        <Label htmlFor="allowBrandingRemoval" className="cursor-pointer">Allow branding removal</Label>
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label htmlFor="stripePriceIdMonthly" className="text-xs text-muted-foreground">
            Stripe Monthly Price ID
          </Label>
          <Input
            id="stripePriceIdMonthly"
            placeholder="price_…"
            {...register("stripePriceIdMonthly")}
            className="text-xs font-mono"
          />
          {errors.stripePriceIdMonthly && (
            <p className="text-xs text-destructive">{errors.stripePriceIdMonthly.message}</p>
          )}
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="stripePriceIdYearly" className="text-xs text-muted-foreground">
            Stripe Yearly Price ID
          </Label>
          <Input
            id="stripePriceIdYearly"
            placeholder="price_…"
            {...register("stripePriceIdYearly")}
            className="text-xs font-mono"
          />
          {errors.stripePriceIdYearly && (
            <p className="text-xs text-destructive">{errors.stripePriceIdYearly.message}</p>
          )}
        </div>
      </div>

      <div className="flex gap-2 pt-1">
        <Button type="submit" size="sm" disabled={saving} className="flex-1">
          {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : "Save"}
        </Button>
        <Button type="button" size="sm" variant="ghost" onClick={onClose}>
          <X className="h-4 w-4" />
        </Button>
      </div>
    </form>
  );
}

function PlanCardSkeleton() {
  return (
    <Card>
      <CardContent className="p-4 space-y-3">
        <div className="flex items-start justify-between gap-4">
          <div className="space-y-2 flex-1">
            <Skeleton className="h-5 w-24" />
            <Skeleton className="h-4 w-40" />
            <Skeleton className="h-5 w-28 rounded-full" />
          </div>
          <div className="flex items-center gap-1.5">
            <Skeleton className="h-6 w-6" />
            <Skeleton className="h-7 w-7 rounded" />
            <Skeleton className="h-7 w-7 rounded" />
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

function PlanCard({ plan }: { plan: PlanResponse }) {
  const [editing,  setEditing]  = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [updatePlan, { isLoading: saving  }] = useUpdatePlanMutation();
  const [deletePlan, { isLoading: removing }] = useDeletePlanMutation();

  async function handleUpdate(values: FormValues) {
    try {
      await updatePlan({ id: plan.id, ...values }).unwrap();
      toast.success("Plan updated");
      setEditing(false);
    } catch {
      toast.error("Failed to update plan");
    }
  }

  async function handleDelete() {
    try {
      await deletePlan(plan.id).unwrap();
      toast.success("Plan deleted");
    } catch {
      toast.error("Failed to delete plan");
    }
  }

  // Compute savings from actual prices — not from the stored yearlyDiscountPercent field,
  // which can silently desync if prices are edited without updating the discount.
  const computedSavingsPct =
    plan.priceMonthly > 0
      ? Math.round((1 - plan.priceYearly / (plan.priceMonthly * 12)) * 100)
      : 0;

  return (
    <Card className="hover:border-border/60 transition-colors">
      <CardContent className="p-4 space-y-3">
        {/* ── Info row ─────────────────────────── */}
        <div className="flex items-start justify-between gap-4">
          {/* left: info */}
          <div className="space-y-1 min-w-0">
            <p className="text-base font-semibold truncate" title={plan.name}>{plan.name}</p>
            <div className="flex items-center gap-1.5 flex-wrap">
              <span className="text-xs text-muted-foreground">
                {plan.billingInterval === "Monthly" ? "Billed monthly" : "Billed yearly only"}
              </span>
              {plan.allowBrandingRemoval && (
                <span className="text-xs px-1.5 py-0.5 rounded-full bg-purple-100 text-purple-700 dark:bg-purple-900/30 dark:text-purple-300">
                  White-label
                </span>
              )}
            </div>
            <div className="space-y-0.5 mt-1">
              <p className="text-sm font-mono">
                <span className="font-medium">
                  {plan.billingInterval === "Monthly"
                    ? formatCurrency(plan.priceMonthly)
                    : formatCurrency(plan.priceYearly)}
                </span>
                <span className="text-xs text-muted-foreground">
                  {plan.billingInterval === "Monthly" ? "/mo" : "/yr"}
                </span>
              </p>
              <p
                className="text-[11px] text-muted-foreground/50 font-mono"
                title="Reference only — not charged at checkout for this plan"
              >
                {plan.billingInterval === "Monthly"
                  ? `${formatCurrency(plan.priceYearly)}/yr ref.`
                  : `${formatCurrency(plan.priceMonthly)}/mo ref.`}
              </p>
              {computedSavingsPct > 0 && (
                <span className="inline-flex items-center text-xs px-2 py-0.5 rounded-full bg-emerald-50 text-emerald-600 dark:bg-emerald-900/20 dark:text-emerald-400 font-medium">
                  Save {computedSavingsPct}% annually
                </span>
              )}
            </div>
          </div>

          {/* right: subscriber count + actions */}
          <div className="flex items-center gap-3 shrink-0">
            <span
              className="flex items-center gap-1 text-xs text-muted-foreground"
              title={`${plan.subscriberCount} studio${plan.subscriberCount !== 1 ? "s" : ""} subscribed`}
              aria-label={`${plan.subscriberCount} studio${plan.subscriberCount !== 1 ? "s" : ""} subscribed to ${plan.name}`}
            >
              <Users className="h-3.5 w-3.5" />
              {plan.subscriberCount}
            </span>

            {!editing && !deleting && (
              <>
                <Button
                  size="sm"
                  variant="ghost"
                  className="h-8 w-8 p-0 text-muted-foreground hover:text-foreground transition-colors"
                  onClick={() => setEditing(true)}
                  aria-label={`Edit ${plan.name} plan`}
                  title="Edit"
                >
                  <Edit2 className="h-3.5 w-3.5" />
                </Button>
                <Button
                  size="sm"
                  variant="ghost"
                  className="h-8 w-8 p-0 text-red-500 dark:text-red-400 hover:text-red-600 dark:hover:text-red-300 hover:bg-red-50 dark:hover:bg-red-900/20 transition-colors"
                  onClick={() => setDeleting(true)}
                  aria-label={`Delete ${plan.name} plan`}
                  title="Delete"
                >
                  <Trash2 className="h-3.5 w-3.5" />
                </Button>
              </>
            )}
          </div>
        </div>

        {/* ── Edit form ────────────────────────── */}
        {editing && (
          <div className="border-t pt-3">
            <PlanForm
              defaultValues={{
                name:                  plan.name,
                billingInterval:       plan.billingInterval,
                priceMonthly:          plan.priceMonthly,
                priceYearly:           plan.priceYearly,
                yearlyDiscountPercent: plan.yearlyDiscountPercent,
                allowBrandingRemoval:  plan.allowBrandingRemoval,
                stripePriceIdMonthly:  plan.stripePriceIdMonthly ?? null,
                stripePriceIdYearly:   plan.stripePriceIdYearly ?? null,
              }}
              onSave={handleUpdate}
              onClose={() => setEditing(false)}
              saving={saving}
            />
          </div>
        )}

        {/* ── Delete confirmation ──────────────── */}
        {deleting && (
          <div className="border-t pt-3 space-y-2">
            {plan.subscriberCount > 0 ? (
              <p className="text-xs text-amber-600 dark:text-amber-400">
                <strong>{plan.subscriberCount} studio{plan.subscriberCount !== 1 ? "s" : ""}</strong>{" "}
                {plan.subscriberCount === 1 ? "is" : "are"} on this plan.
                Deleting it will prevent new signups — existing subscribers are not affected.
              </p>
            ) : (
              <p className="text-xs text-muted-foreground">No active subscribers. Safe to delete.</p>
            )}
            <div className="flex items-center gap-2">
              <span className="text-xs text-destructive font-medium">
                Delete &quot;{plan.name}&quot; permanently?
              </span>
              <Button
                size="sm"
                variant="destructive"
                className="h-7 px-2 text-xs"
                disabled={removing}
                onClick={handleDelete}
              >
                {removing ? <Loader2 className="h-3 w-3 animate-spin" /> : "Yes, delete"}
              </Button>
              <Button
                size="sm"
                variant="ghost"
                className="h-7 px-2 text-xs"
                onClick={() => setDeleting(false)}
              >
                Cancel
              </Button>
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

export function PlanManagementPage() {
  useDocumentMeta({ title: "Plans — Platform Admin", canonical: "/platform/plans" });

  const [creating, setCreating] = useState(false);
  const { data: plans, isLoading, isError } = useGetIssuerPlansQuery();
  const [createPlan, { isLoading: saving }] = useCreatePlanMutation();

  async function handleCreate(values: FormValues) {
    try {
      await createPlan(values).unwrap();
      toast.success("Plan created");
      setCreating(false);
    } catch {
      toast.error("Failed to create plan");
    }
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-20">
        <div className="flex items-center gap-2">
          <CreditCard className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Plans</span>
        </div>
        <Button size="sm" className="gap-1.5" onClick={() => setCreating((v) => !v)}>
          <Plus className="h-4 w-4" />
          New plan
        </Button>
      </header>

      <main className="max-w-4xl mx-auto px-4 py-6">
        {creating && (
          <div className="max-w-xl mb-6">
            <PlanForm
              onSave={handleCreate}
              onClose={() => setCreating(false)}
              saving={saving}
            />
          </div>
        )}

        {isLoading && (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
            <PlanCardSkeleton />
            <PlanCardSkeleton />
            <PlanCardSkeleton />
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">Failed to load plans.</p>
        )}

        {!isLoading && !isError && plans?.length === 0 && !creating && (
          <div className="flex flex-col items-center justify-center py-24 gap-3">
            <CreditCard className="h-10 w-10 text-muted-foreground/30" />
            <p className="text-sm text-muted-foreground">No plans yet.</p>
            <p className="text-xs text-muted-foreground max-w-xs text-center">
              Create your first plan to allow studios to subscribe.
            </p>
            <Button size="sm" variant="outline" className="gap-1.5 mt-2"
              onClick={() => setCreating(true)}>
              <Plus className="h-4 w-4" />
              Create first plan
            </Button>
          </div>
        )}

        {!isLoading && !isError && plans && plans.length > 0 && (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
            {plans.map((p) => (
              <PlanCard key={p.id} plan={p} />
            ))}

            {/* Ghost tile — always last; CSS grid handles wrapping at every breakpoint */}
            <button
              type="button"
              onClick={() => setCreating(true)}
              className="flex flex-col items-center justify-center gap-2 rounded-lg border-2 border-dashed border-border/40 p-8 text-muted-foreground/40 hover:border-border/70 hover:text-muted-foreground/60 transition-colors cursor-pointer min-h-[100px]"
              aria-label="Add a new plan"
            >
              <Plus className="h-5 w-5" />
              <span className="text-xs">New plan</span>
            </button>
          </div>
        )}
      </main>
    </div>
  );
}
