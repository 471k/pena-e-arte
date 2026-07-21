import { useEffect, useState } from "react";
import { useForm, useWatch, type Resolver, type Control, type UseFormRegister, type UseFormSetValue } from "react-hook-form";
import { useNavigate, useParams, useBlocker, Link } from "react-router-dom";
import { toast } from "sonner";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { ArrowLeft, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { ToggleSwitch } from "@/shared/components/ui/toggle-switch";
import {
  AlertDialog,
  AlertDialogContent,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogAction,
  AlertDialogCancel,
} from "@/shared/components/ui/alert-dialog";
import {
  useGetIssuerPlansQuery,
  useCreatePlanMutation,
  useUpdatePlanMutation,
  type PlanPriceRequest,
} from "@/features/billing/billingApi";
import { priceFor, type PlanResponse } from "@/features/billing/billing.types";

// Blank input, undefined, or NaN all mean "unlimited" (null) — anything else must be a
// positive integer. Used for the five Plan usage-limit fields below.
const optionalPositiveInt = z.preprocess((v) => {
  if (v === "" || v === undefined || v === null) return null;
  const n = typeof v === "string" ? Number(v) : v;
  return Number.isNaN(n) ? null : n;
}, z.number().int().positive().nullable());

const priceSectionSchema = z.object({
  enabled:       z.boolean(),
  price:         z.number().min(0).optional(),
  stripePriceId: z.string().max(200).optional().nullable(),
});

const schema = z.object({
  name:                     z.string().min(1, "Name is required").max(100),
  yearlyDiscountPercent:    z.number({ message: "Required" }).min(0).max(100),
  monthly:                  priceSectionSchema,
  yearly:                   priceSectionSchema,
  allowBrandingRemoval:     z.boolean(),
  maxArtists:               optionalPositiveInt,
  maxAppointmentsPerMonth:  optionalPositiveInt,
  maxNotificationsPerMonth: optionalPositiveInt,
  maxStorageGb:             optionalPositiveInt,
  maxLocations:             optionalPositiveInt,
  allowApiAccess:           z.boolean(),
  prioritySupport:          z.boolean(),
}).refine((v) => v.monthly.enabled || v.yearly.enabled, {
  message: "At least one billing interval must be enabled.",
  path: ["monthly"],
}).refine((v) => !v.monthly.enabled || v.monthly.price !== undefined, {
  message: "Price is required when this interval is enabled.",
  path: ["monthly", "price"],
}).refine((v) => !v.yearly.enabled || v.yearly.price !== undefined, {
  message: "Price is required when this interval is enabled.",
  path: ["yearly", "price"],
}).refine((v) => {
  const prices = [v.monthly, v.yearly].filter((s) => s.enabled).map((s) => s.price ?? 0);
  return prices.every((p) => p === 0) || prices.every((p) => p > 0);
}, {
  message: "A plan must be either fully free (both prices = 0) or fully paid (both prices > 0).",
  path: ["monthly", "price"],
});

type FormValues = z.infer<typeof schema>;
type LimitFieldName = "maxArtists" | "maxAppointmentsPerMonth" | "maxNotificationsPerMonth" | "maxStorageGb" | "maxLocations";

const EMPTY_DEFAULTS: FormValues = {
  name:                     "",
  yearlyDiscountPercent:    17,
  monthly:                  { enabled: true, price: undefined, stripePriceId: null },
  yearly:                   { enabled: false, price: undefined, stripePriceId: null },
  allowBrandingRemoval:     false,
  maxArtists:               null,
  maxAppointmentsPerMonth:  null,
  maxNotificationsPerMonth: null,
  maxStorageGb:             null,
  maxLocations:             null,
  allowApiAccess:           false,
  prioritySupport:          false,
};

function planToFormValues(plan: PlanResponse): FormValues {
  const monthly = priceFor(plan, "Monthly");
  const yearly  = priceFor(plan, "Yearly");
  return {
    name:                     plan.name,
    yearlyDiscountPercent:    plan.yearlyDiscountPercent,
    monthly: {
      enabled:       monthly !== undefined,
      price:         monthly?.price,
      stripePriceId: monthly?.stripePriceId ?? null,
    },
    yearly: {
      enabled:       yearly !== undefined,
      price:         yearly?.price,
      stripePriceId: yearly?.stripePriceId ?? null,
    },
    allowBrandingRemoval:     plan.allowBrandingRemoval,
    maxArtists:               plan.maxArtists,
    maxAppointmentsPerMonth:  plan.maxAppointmentsPerMonth,
    maxNotificationsPerMonth: plan.maxNotificationsPerMonth,
    maxStorageGb:             plan.maxStorageGb,
    maxLocations:             plan.maxLocations,
    allowApiAccess:           plan.allowApiAccess,
    prioritySupport:          plan.prioritySupport,
  };
}

function toPrices(values: FormValues): PlanPriceRequest[] {
  const prices: PlanPriceRequest[] = [];
  if (values.monthly.enabled) {
    prices.push({ interval: "Monthly", price: values.monthly.price ?? 0, stripePriceId: values.monthly.stripePriceId });
  }
  if (values.yearly.enabled) {
    prices.push({ interval: "Yearly", price: values.yearly.price ?? 0, stripePriceId: values.yearly.stripePriceId });
  }
  return prices;
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-GB", {
    style:                 "currency",
    currency:              "EUR",
    minimumFractionDigits: 0,
    maximumFractionDigits: 2,
  }).format(amount);
}

// The API returns a single joined FluentValidation message on 422, not a per-field
// payload — so there's nothing to map onto individual RHF fields. Surface it as a
// form-level banner instead of pretending field-level errors exist.
function extractErrorMessage(err: unknown): string {
  if (err && typeof err === "object" && "data" in err) {
    const data = (err as { data?: unknown }).data;
    if (data && typeof data === "object" && "message" in data) {
      const message = (data as { message?: unknown }).message;
      if (typeof message === "string" && message.length > 0) return message;
    }
  }
  return "Something went wrong. Please try again.";
}

interface LimitFieldProps {
  id:       string;
  label:    string;
  name:     LimitFieldName;
  control:  Control<FormValues>;
  register: UseFormRegister<FormValues>;
  setValue: UseFormSetValue<FormValues>;
  error?:   string;
}

function LimitField({ id, label, name, control, register, setValue, error }: LimitFieldProps) {
  const value = useWatch({ control, name });
  const unlimited = value === null || value === undefined;

  return (
    <div className="space-y-1.5">
      <div className="flex items-center justify-between gap-2">
        <Label htmlFor={id} className="text-xs text-muted-foreground">{label}</Label>
        <label className="flex items-center gap-1.5 text-xs text-muted-foreground cursor-pointer select-none">
          <input
            id={`${id}-unlimited`}
            type="checkbox"
            className="h-3.5 w-3.5 rounded border-input accent-primary"
            checked={unlimited}
            onChange={(e) =>
              setValue(name, e.target.checked ? null : 1, { shouldDirty: true, shouldValidate: true })
            }
          />
          Unlimited
        </label>
      </div>
      <Input id={id} type="number" min="1" disabled={unlimited} {...register(name)} />
      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
}

export function PlanEditPage() {
  const navigate = useNavigate();
  const { planId } = useParams<{ planId?: string }>();
  const isEditMode = Boolean(planId);
  const [submitError, setSubmitError] = useState<string | null>(null);

  const { data: plans, isLoading } = useGetIssuerPlansQuery();
  const [createPlan, { isLoading: creating }] = useCreatePlanMutation();
  const [updatePlan, { isLoading: updating }] = useUpdatePlanMutation();
  const saving = creating || updating;

  const plan = isEditMode ? plans?.find((p) => p.id === planId) : undefined;
  const notFound = isEditMode && !isLoading && plans !== undefined && !plan;

  const {
    register, handleSubmit, control, setValue, reset,
    formState: { errors, isDirty },
  } = useForm<FormValues>({
    resolver:      zodResolver(schema) as Resolver<FormValues>,
    defaultValues: EMPTY_DEFAULTS,
  });

  useEffect(() => {
    if (plan) reset(planToFormValues(plan), { keepDirty: false });
  }, [plan, reset]);

  useDocumentMeta({
    title:     isEditMode ? `${plan?.name ?? "Edit plan"} — Platform Admin` : "New plan — Platform Admin",
    canonical: isEditMode && planId ? `/platform/plans/${planId}/edit` : "/platform/plans/new",
  });

  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) => isDirty && currentLocation.pathname !== nextLocation.pathname,
  );

  // Navigating right after reset() in the same event handler would race the blocker:
  // its predicate closure is only refreshed on the next render, so it could still see
  // the pre-reset isDirty=true. Deferring the navigate to an effect guarantees it runs
  // after the reset's re-render has already committed with isDirty=false.
  const [navigateAfterSave, setNavigateAfterSave] = useState(false);
  useEffect(() => {
    if (navigateAfterSave) navigate("/platform/plans");
  }, [navigateAfterSave, navigate]);

  const watchedMonthlyEnabled = useWatch({ control, name: "monthly.enabled" });
  const watchedYearlyEnabled  = useWatch({ control, name: "yearly.enabled" });
  const watchedMonthlyPrice   = useWatch({ control, name: "monthly.price" });
  const watchedDiscount       = useWatch({ control, name: "yearlyDiscountPercent" });
  const watchedBranding       = useWatch({ control, name: "allowBrandingRemoval" });

  const suggestedYearly =
    watchedMonthlyPrice !== undefined && watchedMonthlyPrice > 0 && watchedDiscount >= 0 && watchedDiscount < 100
      ? watchedMonthlyPrice * 12 * (1 - watchedDiscount / 100)
      : null;

  async function onSubmit(values: FormValues) {
    setSubmitError(null);
    const payload = {
      name:                     values.name,
      yearlyDiscountPercent:    values.yearlyDiscountPercent,
      prices:                   toPrices(values),
      allowBrandingRemoval:     values.allowBrandingRemoval,
      maxArtists:               values.maxArtists,
      maxAppointmentsPerMonth:  values.maxAppointmentsPerMonth,
      maxNotificationsPerMonth: values.maxNotificationsPerMonth,
      maxStorageGb:             values.maxStorageGb,
      maxLocations:             values.maxLocations,
      allowApiAccess:           values.allowApiAccess,
      prioritySupport:          values.prioritySupport,
    };

    try {
      if (isEditMode && planId) {
        await updatePlan({ id: planId, ...payload }).unwrap();
        toast.success("Plan updated");
      } else {
        await createPlan(payload).unwrap();
        toast.success("Plan created");
      }
      // Clear dirty state so the unsaved-changes blocker doesn't fire; the actual
      // navigation is deferred to an effect (see navigateAfterSave above).
      reset(values, { keepDirty: false });
      setNavigateAfterSave(true);
    } catch (err) {
      setSubmitError(extractErrorMessage(err));
      toast.error(isEditMode ? "Failed to update plan" : "Failed to create plan");
    }
  }

  if (isEditMode && isLoading) {
    return (
      <div className="min-h-screen bg-background">
        <div className="max-w-3xl mx-auto px-4 py-8 space-y-4">
          <Skeleton className="h-6 w-40" />
          <Skeleton className="h-40 w-full" />
          <Skeleton className="h-40 w-full" />
        </div>
      </div>
    );
  }

  if (notFound) {
    return (
      <div className="min-h-screen bg-background">
        <div className="max-w-3xl mx-auto px-4 py-16 text-center">
          <p className="text-sm text-destructive">Plan not found.</p>
          <Link to="/platform/plans" className="text-sm text-primary hover:underline mt-2 inline-block">
            ← Back to Plans
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background pb-20 md:pb-0">
      <header className="flex items-center justify-between gap-4 px-6 py-3 border-b bg-background sticky top-[var(--issuer-nav-height)] z-10">
        <div className="min-w-0">
          <Link
            to="/platform/plans"
            className="flex items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors mb-1"
          >
            <ArrowLeft className="h-3.5 w-3.5" />
            Plans
          </Link>
          <h1 className="text-base font-semibold truncate">
            {isEditMode ? plan?.name ?? "Edit plan" : "New plan"}
          </h1>
        </div>
        <div className="hidden md:flex items-center gap-2 shrink-0">
          <Button type="button" variant="ghost" size="sm" onClick={() => navigate("/platform/plans")}>
            Cancel
          </Button>
          <Button type="submit" form="plan-edit-form" size="sm" disabled={saving} className="gap-1.5">
            {saving && <Loader2 className="h-4 w-4 animate-spin" />}
            Save
          </Button>
        </div>
      </header>

      <main className="max-w-3xl mx-auto px-4 py-6">
        {submitError && (
          <div role="alert" className="mb-4 rounded-md border border-destructive/40 bg-destructive/10 px-4 py-3 text-sm text-destructive">
            {submitError}
          </div>
        )}

        <form id="plan-edit-form" onSubmit={handleSubmit(onSubmit)} className="space-y-6">
          <Card>
            <CardHeader>
              <CardTitle className="text-sm">Basic info</CardTitle>
            </CardHeader>
            <CardContent className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="planName">Name</Label>
                <Input id="planName" {...register("name")} aria-invalid={!!errors.name} />
                {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="discount">Yearly discount (%)</Label>
                <Input id="discount" type="number" min="0" max="100"
                  {...register("yearlyDiscountPercent", { valueAsNumber: true })} />
                {errors.yearlyDiscountPercent && (
                  <p className="text-xs text-destructive">{errors.yearlyDiscountPercent.message}</p>
                )}
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-sm">Pricing</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="space-y-2">
                <div className="flex items-center gap-2">
                  <ToggleSwitch
                    checked={watchedMonthlyEnabled}
                    onChange={() => setValue("monthly.enabled", !watchedMonthlyEnabled, { shouldDirty: true, shouldValidate: true })}
                    aria-label="Monthly price"
                  />
                  <Label className="font-medium">Monthly price</Label>
                </div>
                {watchedMonthlyEnabled && (
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-3 pl-2">
                    <div className="space-y-1.5">
                      <Label htmlFor="monthlyPrice" className="text-xs text-muted-foreground">Monthly price (€)</Label>
                      <Input id="monthlyPrice" type="number" step="0.01" min="0"
                        {...register("monthly.price", { valueAsNumber: true })} />
                      {errors.monthly?.price && <p className="text-xs text-destructive">{errors.monthly.price.message}</p>}
                    </div>
                    <div className="space-y-1.5">
                      <Label htmlFor="monthlyStripePriceId" className="text-xs text-muted-foreground">Stripe Monthly Price ID</Label>
                      <Input id="monthlyStripePriceId" placeholder="price_…" className="text-xs font-mono"
                        {...register("monthly.stripePriceId")} />
                    </div>
                  </div>
                )}
              </div>

              <div className="space-y-2 border-t pt-4">
                <div className="flex items-center gap-2">
                  <ToggleSwitch
                    checked={watchedYearlyEnabled}
                    onChange={() => setValue("yearly.enabled", !watchedYearlyEnabled, { shouldDirty: true, shouldValidate: true })}
                    aria-label="Yearly price"
                  />
                  <Label className="font-medium">Yearly price</Label>
                </div>
                {watchedYearlyEnabled && (
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-3 pl-2">
                    <div className="space-y-1.5">
                      <Label htmlFor="yearlyPrice" className="text-xs text-muted-foreground">Yearly price (€)</Label>
                      <Input id="yearlyPrice" type="number" step="0.01" min="0"
                        {...register("yearly.price", { valueAsNumber: true })} />
                      {suggestedYearly !== null && (
                        <p className="text-[11px] text-muted-foreground">
                          Suggested: {formatCurrency(suggestedYearly)} (monthly × 12 × {100 - watchedDiscount}%)
                        </p>
                      )}
                      {errors.yearly?.price && <p className="text-xs text-destructive">{errors.yearly.price.message}</p>}
                    </div>
                    <div className="space-y-1.5">
                      <Label htmlFor="yearlyStripePriceId" className="text-xs text-muted-foreground">Stripe Yearly Price ID</Label>
                      <Input id="yearlyStripePriceId" placeholder="price_…" className="text-xs font-mono"
                        {...register("yearly.stripePriceId")} />
                    </div>
                  </div>
                )}
                {errors.monthly?.message && (
                  <p className="text-xs text-destructive">{errors.monthly.message}</p>
                )}
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-sm">Feature flags</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3">
              <div className="flex items-center gap-2">
                <ToggleSwitch
                  checked={watchedBranding}
                  onChange={() => setValue("allowBrandingRemoval", !watchedBranding, { shouldDirty: true })}
                  aria-label="Allow branding removal"
                />
                <Label>Allow branding removal</Label>
              </div>
              {/* allowApiAccess toggle intentionally hidden — no API/webhook subsystem exists yet */}
              {/* prioritySupport toggle intentionally hidden — no support-priority routing
                  is implemented; same reasoning as allowApiAccess above. */}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-sm">Usage limits</CardTitle>
            </CardHeader>
            <CardContent className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <LimitField id="maxArtists" label="Artists" name="maxArtists"
                control={control} register={register} setValue={setValue}
                error={errors.maxArtists?.message} />
              <LimitField id="maxAppointmentsPerMonth" label="Appointments/mo" name="maxAppointmentsPerMonth"
                control={control} register={register} setValue={setValue}
                error={errors.maxAppointmentsPerMonth?.message} />
              <LimitField id="maxNotificationsPerMonth" label="Notifications/mo" name="maxNotificationsPerMonth"
                control={control} register={register} setValue={setValue}
                error={errors.maxNotificationsPerMonth?.message} />
              <LimitField id="maxStorageGb" label="Storage (GB)" name="maxStorageGb"
                control={control} register={register} setValue={setValue}
                error={errors.maxStorageGb?.message} />
              <LimitField id="maxLocations" label="Locations" name="maxLocations"
                control={control} register={register} setValue={setValue}
                error={errors.maxLocations?.message} />
            </CardContent>
          </Card>
        </form>
      </main>

      {/* Sticky mobile footer — desktop uses the header actions instead */}
      <div className="md:hidden fixed bottom-0 inset-x-0 z-10 flex gap-2 border-t bg-background px-4 py-3">
        <Button type="button" variant="ghost" className="flex-1" onClick={() => navigate("/platform/plans")}>
          Cancel
        </Button>
        <Button type="submit" form="plan-edit-form" className="flex-1 gap-1.5" disabled={saving}>
          {saving && <Loader2 className="h-4 w-4 animate-spin" />}
          Save
        </Button>
      </div>

      {blocker.state === "blocked" && (
        <AlertDialog open onOpenChange={(open) => { if (!open) blocker.reset(); }}>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>Discard unsaved changes?</AlertDialogTitle>
              <AlertDialogDescription>
                You have unsaved changes to this plan. Leaving now will discard them.
              </AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel onClick={() => blocker.reset()}>Keep editing</AlertDialogCancel>
              <AlertDialogAction onClick={() => blocker.proceed()}>Discard changes</AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      )}
    </div>
  );
}
