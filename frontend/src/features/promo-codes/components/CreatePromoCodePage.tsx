import { useNavigate } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { ArrowLeft, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { cn } from "@/shared/utils/cn";
import { useCreatePromoCodeMutation } from "../promoCodesApi";
import type { CreatePromoCodeRequest } from "../promoCode.types";

const createSchema = z
  .object({
    code:            z.string().min(1, "Code is required").max(40, "Max 40 characters"),
    discountType:    z.enum(["fixed", "percent"]),
    amount:          z.number({ error: "Amount is required" }).positive("Must be greater than 0"),
    isActive:        z.boolean(),
    expiresAt:       z.string().nullable(),
    maxRedemptions:  z.number().positive("Must be greater than 0").nullable(),
  })
  .superRefine((data, ctx) => {
    if (data.discountType === "percent" && data.amount > 100) {
      ctx.addIssue({ code: z.ZodIssueCode.custom, message: "Must be between 0.01 and 100", path: ["amount"] });
    }
  });

type CreateFormValues = z.infer<typeof createSchema>;

export function CreatePromoCodePage() {
  const navigate = useNavigate();
  const [createPromoCode, { isLoading }] = useCreatePromoCodeMutation();

  const {
    register,
    handleSubmit,
    watch,
    formState: { errors },
  } = useForm<CreateFormValues>({
    resolver: zodResolver(createSchema),
    defaultValues: {
      discountType: "fixed",
      isActive: true,
      expiresAt: null,
      maxRedemptions: null,
    },
  });

  const discountType = watch("discountType");

  async function onSubmit(values: CreateFormValues) {
    const body: CreatePromoCodeRequest = {
      code:           values.code.toUpperCase(),
      amountFixed:    values.discountType === "fixed"   ? values.amount : null,
      amountPercent:  values.discountType === "percent" ? values.amount : null,
      isActive:       values.isActive,
      expiresAt:      values.expiresAt ? new Date(values.expiresAt).toISOString() : null,
      maxRedemptions: values.maxRedemptions,
    };
    const result = await createPromoCode(body);
    if ("data" in result) {
      toast.success("Promo code created.");
      navigate(`/promo-codes/${result.data!.id}`);
    } else {
      toast.error("Failed to create promo code.");
    }
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/promo-codes")}
          className="gap-1.5"
          disabled={isLoading}
        >
          <ArrowLeft className="h-4 w-4" />
          Promo Codes
        </Button>
      </header>

      <main className="max-w-lg mx-auto px-4 py-8">
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
          <h2 className="text-base font-semibold">New Promo Code</h2>

          <div className="space-y-1.5">
            <Label htmlFor="code">Code</Label>
            <Input
              id="code"
              placeholder="e.g. SUMMER20"
              className={cn("uppercase", errors.code && "border-destructive")}
              {...register("code")}
            />
            {errors.code && (
              <p className="text-xs text-destructive-text">{errors.code.message}</p>
            )}
            <p className="text-xs text-muted-foreground">
              What clients type at booking. Not case-sensitive.
            </p>
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
            <Label htmlFor="amount">
              {discountType === "fixed" ? "Discount (€)" : "Discount (%)"}
            </Label>
            <Input
              id="amount"
              type="number"
              step="0.01"
              min="0.01"
              placeholder={discountType === "fixed" ? "e.g. 20" : "e.g. 10"}
              {...register("amount", { valueAsNumber: true })}
              className={cn(errors.amount && "border-destructive")}
            />
            {errors.amount && (
              <p className="text-xs text-destructive-text">{errors.amount.message}</p>
            )}
            <p className="text-xs text-muted-foreground">
              Applied against the client's deposit amount, floored at €0.
            </p>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="expiresAt">Expires on (optional)</Label>
            <Input
              id="expiresAt"
              type="date"
              {...register("expiresAt")}
              className={cn(errors.expiresAt && "border-destructive")}
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="maxRedemptions">Max redemptions (optional)</Label>
            <Input
              id="maxRedemptions"
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
              id="isActive"
              {...register("isActive")}
              className="h-4 w-4 rounded border-input accent-primary"
            />
            <span className="text-sm">Active</span>
          </label>

          <Button type="submit" className="w-full" disabled={isLoading}>
            {isLoading ? (
              <>
                <Loader2 className="h-4 w-4 animate-spin" />
                Creating…
              </>
            ) : (
              "Create Code"
            )}
          </Button>
        </form>
      </main>
    </div>
  );
}
