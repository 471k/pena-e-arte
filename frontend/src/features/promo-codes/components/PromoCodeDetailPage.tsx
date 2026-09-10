import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
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
import { Skeleton } from "@/shared/components/ui/skeleton";
import { cn } from "@/shared/utils/cn";
import {
  useGetPromoCodeByIdQuery,
  useUpdatePromoCodeMutation,
  useDeletePromoCodeMutation,
} from "../promoCodesApi";
import type { UpdatePromoCodeRequest } from "../promoCode.types";

const editSchema = z
  .object({
    code:           z.string().min(1, "Code is required").max(40, "Max 40 characters"),
    discountType:   z.enum(["fixed", "percent"]),
    amount:         z.number({ error: "Amount is required" }).positive("Must be greater than 0"),
    isActive:       z.boolean(),
    expiresAt:      z.string().nullable(),
    maxRedemptions: z.number().positive("Must be greater than 0").nullable(),
  })
  .superRefine((data, ctx) => {
    if (data.discountType === "percent" && data.amount > 100) {
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

function toDateInputValue(iso: string | null): string {
  return iso ? iso.slice(0, 10) : "";
}

export function PromoCodeDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const { data: promoCode, isLoading, isError } = useGetPromoCodeByIdQuery(id!);
  const [updatePromoCode, { isLoading: isSaving }] = useUpdatePromoCodeMutation();
  const [deletePromoCode, { isLoading: isDeleting }] = useDeletePromoCodeMutation();

  const [mode, setMode] = useState<"view" | "edit" | "confirm-delete">("view");

  const {
    register,
    handleSubmit,
    watch,
    formState: { errors },
    reset,
  } = useForm<EditFormValues>({ resolver: zodResolver(editSchema) });

  const discountType = watch("discountType");

  function startEdit() {
    if (!promoCode) return;
    reset({
      code:           promoCode.code,
      discountType:   promoCode.amountFixed !== null ? "fixed" : "percent",
      amount:         (promoCode.amountFixed ?? promoCode.amountPercent) as number,
      isActive:       promoCode.isActive,
      expiresAt:      toDateInputValue(promoCode.expiresAt),
      maxRedemptions: promoCode.maxRedemptions,
    });
    setMode("edit");
  }

  async function onSave(values: EditFormValues) {
    if (!id) return;
    const body: UpdatePromoCodeRequest = {
      code:           values.code.toUpperCase(),
      amountFixed:    values.discountType === "fixed"   ? values.amount : null,
      amountPercent:  values.discountType === "percent" ? values.amount : null,
      isActive:       values.isActive,
      expiresAt:      values.expiresAt ? new Date(values.expiresAt).toISOString() : null,
      maxRedemptions: values.maxRedemptions,
    };
    const result = await updatePromoCode({ id, body });
    if ("data" in result) {
      toast.success("Promo code updated.");
      setMode("view");
    } else {
      toast.error("Failed to update promo code.");
    }
  }

  async function onDelete() {
    if (!id) return;
    const result = await deletePromoCode(id);
    if ("error" in result) {
      toast.error("Failed to delete promo code.");
      return;
    }
    navigate("/promo-codes");
  }

  if (isLoading) {
    return (
      <div className="min-h-screen bg-background" aria-label="Loading promo code">
        <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
          <Skeleton className="h-8 w-32" />
        </header>
        <main className="max-w-lg mx-auto px-4 py-8 space-y-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <div key={i} className="space-y-1.5">
              <Skeleton className="h-4 w-24" />
              <Skeleton className="h-10 w-full rounded-md" />
            </div>
          ))}
        </main>
      </div>
    );
  }

  if (isError || !promoCode) {
    return (
      <div className="min-h-screen bg-background flex flex-col items-center justify-center gap-4">
        <p className="text-sm text-destructive-text">Promo code not found.</p>
        <Button variant="ghost" size="sm" onClick={() => navigate("/promo-codes")}>
          <ArrowLeft className="h-4 w-4 mr-1" />
          Back to Promo Codes
        </Button>
      </div>
    );
  }

  const isFixed = promoCode.amountFixed !== null;

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/promo-codes")}
          className="gap-1.5"
        >
          <ArrowLeft className="h-4 w-4" />
          Promo Codes
        </Button>

        {mode === "view" && (
          <div className="flex items-center gap-2">
            <Button variant="outline" size="sm" onClick={startEdit} className="gap-1.5">
              <Pencil className="h-3.5 w-3.5" />
              Edit
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setMode("confirm-delete")}
              className="gap-1.5 text-destructive-text hover:text-destructive-text"
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
                <h1 className="text-lg font-semibold leading-tight font-mono">{promoCode.code}</h1>
                <span className={cn(
                  "text-xs px-2 py-0.5 rounded-full font-medium",
                  promoCode.isActive ? "bg-green-500/10 text-green-700" : "bg-muted text-muted-foreground",
                )}>
                  {promoCode.isActive ? "Active" : "Inactive"}
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
                    {isFixed ? "Fixed" : "Percentage"} · {formatAmount(promoCode.amountFixed, promoCode.amountPercent)}
                  </span>
                </div>
                <div className="text-xs text-muted-foreground pt-1 border-t space-y-1">
                  <p>
                    Expires: {promoCode.expiresAt ? formatDate(promoCode.expiresAt) : "Never"}
                  </p>
                  <p>
                    Redemptions: {promoCode.redemptionCount}
                    {promoCode.maxRedemptions !== null && ` / ${promoCode.maxRedemptions}`}
                  </p>
                </div>
                <div className="flex items-center gap-2 text-xs text-muted-foreground pt-1 border-t">
                  <Calendar className="h-3.5 w-3.5 shrink-0" />
                  <span>Created {formatDate(promoCode.createdAt)}</span>
                </div>
              </CardContent>
            </Card>

            <p className="text-xs text-muted-foreground text-center">
              Last updated {formatDate(promoCode.updatedAt)}
            </p>
          </>
        )}

        {mode === "edit" && (
          <form onSubmit={handleSubmit(onSave)} className="space-y-5">
            <h2 className="text-base font-semibold">Edit Promo Code</h2>

            <div className="space-y-1.5">
              <Label htmlFor="edit-code">Code</Label>
              <Input
                id="edit-code"
                className={cn("uppercase", errors.code && "border-destructive")}
                {...register("code")}
              />
              {errors.code && (
                <p className="text-xs text-destructive-text">{errors.code.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label>Discount type</Label>
              <div className="flex gap-4">
                <label className="flex items-center gap-1.5 cursor-pointer text-sm">
                  <input type="radio" value="fixed" {...register("discountType")} className="accent-primary" />
                  Fixed amount
                </label>
                <label className="flex items-center gap-1.5 cursor-pointer text-sm">
                  <input type="radio" value="percent" {...register("discountType")} className="accent-primary" />
                  Percentage
                </label>
              </div>
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="edit-amount">
                {discountType === "fixed" ? "Discount (€)" : "Discount (%)"}
              </Label>
              <Input
                id="edit-amount"
                type="number"
                step="0.01"
                min="0.01"
                {...register("amount", { valueAsNumber: true })}
                className={cn(errors.amount && "border-destructive")}
              />
              {errors.amount && (
                <p className="text-xs text-destructive-text">{errors.amount.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="edit-expiresAt">Expires on (optional)</Label>
              <Input
                id="edit-expiresAt"
                type="date"
                {...register("expiresAt")}
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="edit-maxRedemptions">Max redemptions (optional)</Label>
              <Input
                id="edit-maxRedemptions"
                type="number"
                step="1"
                min="1"
                placeholder="Unlimited"
                {...register("maxRedemptions", {
                  setValueAs: (v) => (v === "" || v === null || v === undefined ? null : Number(v)),
                })}
                className={cn(errors.maxRedemptions && "border-destructive")}
              />
              {errors.maxRedemptions && (
                <p className="text-xs text-destructive-text">{errors.maxRedemptions.message}</p>
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
              <p className="text-sm font-medium">Delete "{promoCode.code}"?</p>
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
