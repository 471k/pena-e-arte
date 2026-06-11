import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { CreditCard, Edit2, Loader2, Plus, Trash2, X } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { cn } from "@/shared/utils/cn";
import {
  useGetIssuerPlansQuery,
  useCreatePlanMutation,
  useUpdatePlanMutation,
  useDeletePlanMutation,
} from "@/features/billing/billingApi";
import type { PlanResponse } from "@/features/billing/billing.types";

const schema = z.object({
  name:                 z.string().min(1, "Name is required").max(100),
  billingInterval:      z.enum(["Monthly", "Yearly"]),
  priceMonthly:         z.number({ message: "Required" }).positive(),
  priceYearly:          z.number({ message: "Required" }).positive(),
  yearlyDiscountPercent: z.number({ message: "Required" }).min(0).max(100),
});

type FormValues = z.infer<typeof schema>;

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(amount);
}

interface PlanFormProps {
  defaultValues?: Partial<FormValues>;
  onSave:  (v: FormValues) => Promise<void>;
  onClose: () => void;
  saving:  boolean;
}

function PlanForm({ defaultValues, onSave, onClose, saving }: PlanFormProps) {
  const { register, handleSubmit, formState: { errors } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { billingInterval: "Monthly", yearlyDiscountPercent: 17, ...defaultValues },
  });

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

function PlanCard({ plan }: { plan: PlanResponse }) {
  const [editing,  setEditing]  = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [updatePlan, { isLoading: saving  }] = useUpdatePlanMutation();
  const [deletePlan, { isLoading: removing }] = useDeletePlanMutation();

  async function handleUpdate(values: FormValues) {
    await updatePlan({ id: plan.id, ...values }).unwrap();
    setEditing(false);
  }

  async function handleDelete() {
    if (!deleting) { setDeleting(true); return; }
    await deletePlan(plan.id);
  }

  if (editing) {
    return (
      <PlanForm
        defaultValues={{
          name:                 plan.name,
          billingInterval:      plan.billingInterval,
          priceMonthly:         plan.priceMonthly,
          priceYearly:          plan.priceYearly,
          yearlyDiscountPercent: plan.yearlyDiscountPercent,
        }}
        onSave={handleUpdate}
        onClose={() => setEditing(false)}
        saving={saving}
      />
    );
  }

  return (
    <Card>
      <CardContent className="p-4 flex items-start justify-between gap-4">
        <div className="space-y-0.5 min-w-0">
          <div className="flex items-center gap-2">
            <span className="font-medium text-sm">{plan.name}</span>
            <span className="text-xs text-muted-foreground">{plan.billingInterval}</span>
          </div>
          <p className="text-xs text-muted-foreground">
            {formatCurrency(plan.priceMonthly)}/mo · {formatCurrency(plan.priceYearly)}/yr
            {" · "}{plan.yearlyDiscountPercent}% yearly discount
          </p>
        </div>
        <div className="flex gap-1.5 shrink-0">
          <Button size="sm" variant="ghost" className="h-7 w-7 p-0" onClick={() => setEditing(true)}>
            <Edit2 className="h-3.5 w-3.5" />
          </Button>
          {deleting ? (
            <>
              <span className="text-xs text-destructive self-center">Delete?</span>
              <Button size="sm" variant="destructive" className="h-7 px-2 text-xs"
                disabled={removing} onClick={handleDelete}>
                {removing ? <Loader2 className="h-3 w-3 animate-spin" /> : "Yes"}
              </Button>
              <Button size="sm" variant="ghost" className="h-7 px-2 text-xs"
                onClick={() => setDeleting(false)}>No</Button>
            </>
          ) : (
            <Button size="sm" variant="ghost" className="h-7 w-7 p-0 text-muted-foreground"
              onClick={() => setDeleting(true)}>
              <Trash2 className="h-3.5 w-3.5" />
            </Button>
          )}
        </div>
      </CardContent>
    </Card>
  );
}

export function PlanManagementPage() {
  const [creating, setCreating] = useState(false);
  const { data: plans, isLoading, isError } = useGetIssuerPlansQuery();
  const [createPlan, { isLoading: saving }] = useCreatePlanMutation();

  async function handleCreate(values: FormValues) {
    await createPlan(values).unwrap();
    setCreating(false);
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <CreditCard className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Plans</span>
        </div>
        <Button size="sm" variant="outline" className="gap-1.5" onClick={() => setCreating((v) => !v)}>
          <Plus className="h-4 w-4" />
          New plan
        </Button>
      </header>

      <main className="max-w-xl mx-auto px-4 py-6 space-y-3">
        {creating && (
          <PlanForm
            onSave={handleCreate}
            onClose={() => setCreating(false)}
            saving={saving}
          />
        )}

        {isLoading && (
          <div className="flex items-center justify-center py-16 gap-2 text-muted-foreground">
            <Loader2 className="h-5 w-5 animate-spin" />
            <span className="text-sm">Loading…</span>
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">Failed to load plans.</p>
        )}

        {!isLoading && !isError && plans?.length === 0 && !creating && (
          <p className="text-center text-sm text-muted-foreground py-16">No plans yet.</p>
        )}

        {!isLoading && !isError && plans?.map((p) => (
          <PlanCard key={p.id} plan={p} />
        ))}
      </main>
    </div>
  );
}
