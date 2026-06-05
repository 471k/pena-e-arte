import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import {
  ArrowLeft,
  Calendar,
  DollarSign,
  Loader2,
  Pencil,
  Percent,
  Trash2,
} from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { cn } from "@/shared/utils/cn";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import {
  useGetDepositRuleByIdQuery,
  useUpdateDepositRuleMutation,
  useDeleteDepositRuleMutation,
} from "../depositRulesApi";
import type { UpdateDepositRuleRequest } from "../depositRule.types";

const editSchema = z
  .object({
    name:        z.string().min(1, "Name is required").max(100, "Max 100 characters"),
    depositType: z.enum(["fixed", "percent"]),
    amount:      z.coerce.number({ invalid_type_error: "Must be a number" }).positive("Must be greater than 0"),
    isActive:    z.boolean(),
  })
  .superRefine((data, ctx) => {
    if (data.depositType === "percent" && data.amount > 100) {
      ctx.addIssue({ code: z.ZodIssueCode.custom, message: "Must be between 0.01 and 100", path: ["amount"] });
    }
  });

type EditFormValues = z.infer<typeof editSchema>;

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

function formatAmount(amountFixed: number | null, amountPercent: number | null): string {
  if (amountFixed !== null) {
    return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(amountFixed);
  }
  return `${amountPercent}%`;
}

export function DepositRuleDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const canManage = usePermission(Role.Owner);

  const { data: rule, isLoading, isError } = useGetDepositRuleByIdQuery(id!);
  const [updateRule, { isLoading: isSaving }] = useUpdateDepositRuleMutation();
  const [deleteRule, { isLoading: isDeleting }] = useDeleteDepositRuleMutation();

  const [mode, setMode] = useState<"view" | "edit" | "confirm-delete">("view");

  const {
    register,
    handleSubmit,
    watch,
    formState: { errors },
    reset,
  } = useForm<EditFormValues>({ resolver: zodResolver(editSchema) });

  const depositType = watch("depositType");

  function startEdit() {
    if (!rule) return;
    reset({
      name:        rule.name,
      depositType: rule.amountFixed !== null ? "fixed" : "percent",
      amount:      (rule.amountFixed ?? rule.amountPercent) as number,
      isActive:    rule.isActive,
    });
    setMode("edit");
  }

  async function onSave(values: EditFormValues) {
    if (!id) return;
    const body: UpdateDepositRuleRequest = {
      name:          values.name,
      amountFixed:   values.depositType === "fixed"   ? values.amount : null,
      amountPercent: values.depositType === "percent" ? values.amount : null,
      isActive:      values.isActive,
    };
    await updateRule({ id, body });
    setMode("view");
  }

  async function onDelete() {
    if (!id) return;
    await deleteRule(id);
    navigate("/deposit-rules");
  }

  if (isLoading) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center gap-2 text-muted-foreground">
        <Loader2 className="h-5 w-5 animate-spin" />
        <span className="text-sm">Loading…</span>
      </div>
    );
  }

  if (isError || !rule) {
    return (
      <div className="min-h-screen bg-background flex flex-col items-center justify-center gap-4">
        <p className="text-sm text-destructive">Deposit rule not found.</p>
        <Button variant="ghost" size="sm" onClick={() => navigate("/deposit-rules")}>
          <ArrowLeft className="h-4 w-4 mr-1" />
          Back to Deposit Rules
        </Button>
      </div>
    );
  }

  const isFixed = rule.amountFixed !== null;

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/deposit-rules")}
          className="gap-1.5"
        >
          <ArrowLeft className="h-4 w-4" />
          Deposit Rules
        </Button>

        {canManage && mode === "view" && (
          <div className="flex items-center gap-2">
            <Button variant="outline" size="sm" onClick={startEdit} className="gap-1.5">
              <Pencil className="h-3.5 w-3.5" />
              Edit
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setMode("confirm-delete")}
              className="gap-1.5 text-destructive hover:text-destructive"
            >
              <Trash2 className="h-3.5 w-3.5" />
              Delete
            </Button>
          </div>
        )}

        {mode === "edit" && (
          <Button variant="ghost" size="sm" onClick={() => setMode("view")} disabled={isSaving}>
            Cancel
          </Button>
        )}
      </header>

      <main className="max-w-lg mx-auto px-4 py-8 space-y-6">
        {mode === "view" && (
          <>
            <div className="flex items-center gap-4">
              <div className={cn(
                "flex h-14 w-14 shrink-0 items-center justify-center rounded-full",
                isFixed ? "bg-blue-500/10 text-blue-600" : "bg-purple-500/10 text-purple-600",
              )}>
                {isFixed ? <DollarSign className="h-6 w-6" /> : <Percent className="h-6 w-6" />}
              </div>
              <div className="space-y-1.5">
                <h1 className="text-lg font-semibold leading-tight">{rule.name}</h1>
                <span className={cn(
                  "text-xs px-2 py-0.5 rounded-full font-medium",
                  rule.isActive ? "bg-green-500/10 text-green-700" : "bg-muted text-muted-foreground",
                )}>
                  {rule.isActive ? "Active" : "Inactive"}
                </span>
              </div>
            </div>

            <Card>
              <CardContent className="p-4 space-y-3">
                <div className="flex items-center gap-2 text-sm">
                  {isFixed
                    ? <DollarSign className="h-4 w-4 shrink-0 text-muted-foreground" />
                    : <Percent    className="h-4 w-4 shrink-0 text-muted-foreground" />
                  }
                  <span>
                    {isFixed ? "Fixed" : "Percentage"} · {formatAmount(rule.amountFixed, rule.amountPercent)}
                  </span>
                </div>
                <div className="flex items-center gap-2 text-xs text-muted-foreground pt-1 border-t">
                  <Calendar className="h-3.5 w-3.5 shrink-0" />
                  <span>Created {formatDate(rule.createdAt)}</span>
                </div>
              </CardContent>
            </Card>

            {canManage && (
              <p className="text-xs text-muted-foreground text-center">
                Last updated {formatDate(rule.updatedAt)}
              </p>
            )}
          </>
        )}

        {mode === "edit" && (
          <form onSubmit={handleSubmit(onSave)} className="space-y-5">
            <h2 className="text-base font-semibold">Edit Deposit Rule</h2>

            <div className="space-y-1.5">
              <Label htmlFor="edit-name">Rule name</Label>
              <Input
                id="edit-name"
                {...register("name")}
                className={cn(errors.name && "border-destructive")}
              />
              {errors.name && (
                <p className="text-xs text-destructive">{errors.name.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label>Deposit type</Label>
              <div className="flex gap-4">
                <label className="flex items-center gap-1.5 cursor-pointer text-sm">
                  <input type="radio" value="fixed" {...register("depositType")} className="accent-primary" />
                  Fixed amount
                </label>
                <label className="flex items-center gap-1.5 cursor-pointer text-sm">
                  <input type="radio" value="percent" {...register("depositType")} className="accent-primary" />
                  Percentage
                </label>
              </div>
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="edit-amount">
                {depositType === "fixed" ? "Amount (€)" : "Percentage (%)"}
              </Label>
              <Input
                id="edit-amount"
                type="number"
                step="0.01"
                min="0.01"
                {...register("amount")}
                className={cn(errors.amount && "border-destructive")}
              />
              {errors.amount && (
                <p className="text-xs text-destructive">{errors.amount.message}</p>
              )}
            </div>

            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                id="edit-isActive"
                {...register("isActive")}
                className="h-4 w-4 rounded border-input accent-primary"
              />
              <span className="text-sm">Active</span>
            </label>

            <Button type="submit" className="w-full" disabled={isSaving}>
              {isSaving ? (
                <>
                  <Loader2 className="h-4 w-4 animate-spin" />
                  Saving…
                </>
              ) : (
                "Save Changes"
              )}
            </Button>
          </form>
        )}

        {mode === "confirm-delete" && (
          <Card>
            <CardContent className="p-5 space-y-4">
              <p className="text-sm font-medium">Delete "{rule.name}"?</p>
              <p className="text-xs text-muted-foreground">This action cannot be undone.</p>
              <div className="flex gap-2">
                <Button
                  variant="destructive"
                  size="sm"
                  disabled={isDeleting}
                  onClick={onDelete}
                  className="flex-1"
                >
                  {isDeleting ? (
                    <>
                      <Loader2 className="h-4 w-4 animate-spin" />
                      Deleting…
                    </>
                  ) : (
                    "Delete"
                  )}
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  disabled={isDeleting}
                  onClick={() => setMode("view")}
                  className="flex-1"
                >
                  Cancel
                </Button>
              </div>
            </CardContent>
          </Card>
        )}
      </main>
    </div>
  );
}
