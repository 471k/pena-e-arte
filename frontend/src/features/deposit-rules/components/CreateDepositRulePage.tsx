import { useNavigate } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { ArrowLeft, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { cn } from "@/shared/utils/cn";
import { useCreateDepositRuleMutation } from "../depositRulesApi";
import type { CreateDepositRuleRequest } from "../depositRule.types";

const createSchema = z
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

type CreateFormValues = z.infer<typeof createSchema>;

export function CreateDepositRulePage() {
  const navigate = useNavigate();
  const [createRule, { isLoading }] = useCreateDepositRuleMutation();

  const {
    register,
    handleSubmit,
    watch,
    formState: { errors },
  } = useForm<CreateFormValues>({
    resolver: zodResolver(createSchema),
    defaultValues: { depositType: "fixed", isActive: true },
  });

  const depositType = watch("depositType");

  async function onSubmit(values: CreateFormValues) {
    const body: CreateDepositRuleRequest = {
      name:          values.name,
      amountFixed:   values.depositType === "fixed"   ? values.amount : null,
      amountPercent: values.depositType === "percent" ? values.amount : null,
      isActive:      values.isActive,
    };
    const result = await createRule(body);
    if ("data" in result) {
      navigate(`/deposit-rules/${result.data.id}`);
    }
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/deposit-rules")}
          className="gap-1.5"
          disabled={isLoading}
        >
          <ArrowLeft className="h-4 w-4" />
          Deposit Rules
        </Button>
      </header>

      <main className="max-w-lg mx-auto px-4 py-8">
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
          <h2 className="text-base font-semibold">New Deposit Rule</h2>

          <div className="space-y-1.5">
            <Label htmlFor="name">Rule name</Label>
            <Input
              id="name"
              placeholder="e.g. Standard Deposit"
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
            <Label htmlFor="amount">
              {depositType === "fixed" ? "Amount (€)" : "Percentage (%)"}
            </Label>
            <Input
              id="amount"
              type="number"
              step="0.01"
              min="0.01"
              placeholder={depositType === "fixed" ? "e.g. 50" : "e.g. 20"}
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
              "Create Rule"
            )}
          </Button>
        </form>
      </main>
    </div>
  );
}
