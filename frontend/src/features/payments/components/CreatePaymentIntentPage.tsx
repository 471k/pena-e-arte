import { useEffect } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { ArrowLeft, CreditCard, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { cn } from "@/shared/utils/cn";
import { useCreatePaymentIntentMutation } from "../paymentsApi";

const schema = z.object({
  appointmentId: z.string().uuid("Must be a valid UUID"),
  clientId:      z.string().uuid("Must be a valid UUID"),
  amount:        z.number({ error: "Must be a number" }).positive("Must be greater than 0"),
  currency:      z.string().min(3, "Required").max(3, "3-letter code, e.g. EUR"),
});

type FormValues = z.infer<typeof schema>;

export function CreatePaymentIntentPage() {
  const navigate         = useNavigate();
  const [searchParams]   = useSearchParams();

  const defaultAppointmentId = searchParams.get("appointmentId") ?? "";
  const defaultClientId      = searchParams.get("clientId")      ?? "";
  const defaultAmount        = parseFloat(searchParams.get("amount") ?? "") || undefined;

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      appointmentId: defaultAppointmentId,
      clientId:      defaultClientId,
      amount:        defaultAmount,
      currency:      "EUR",
    },
  });

  const [createIntent, { isLoading, data: result, isError }] =
    useCreatePaymentIntentMutation();

  async function onSubmit(values: FormValues) {
    await createIntent(values);
  }

  useEffect(() => {
    if (result) {
      navigate(`/payments/${defaultAppointmentId || result.paymentId}`, { replace: true });
    }
  }, [result, navigate, defaultAppointmentId]);

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/payments")}
          className="gap-1.5"
        >
          <ArrowLeft className="h-4 w-4" />
          Payments
        </Button>
      </header>

      <main className="max-w-md mx-auto px-4 py-8">
        <div className="flex items-center gap-3 mb-6">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-primary/10">
            <CreditCard className="h-5 w-5 text-primary" />
          </div>
          <h1 className="text-lg font-semibold">Create payment intent</h1>
        </div>

        {isError && (
          <Card className="mb-4">
            <CardContent className="p-3">
              <p className="text-sm text-destructive">
                Failed to create payment intent. The appointment may already have a payment.
              </p>
            </CardContent>
          </Card>
        )}

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
          <div className="space-y-1.5">
            <Label htmlFor="appointmentId">Appointment ID</Label>
            <Input
              id="appointmentId"
              {...register("appointmentId")}
              placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
              className={cn(errors.appointmentId && "border-destructive")}
            />
            {errors.appointmentId && (
              <p className="text-xs text-destructive">{errors.appointmentId.message}</p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="clientId">Client ID</Label>
            <Input
              id="clientId"
              {...register("clientId")}
              placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
              className={cn(errors.clientId && "border-destructive")}
            />
            {errors.clientId && (
              <p className="text-xs text-destructive">{errors.clientId.message}</p>
            )}
          </div>

          <div className="flex gap-3">
            <div className="flex-1 space-y-1.5">
              <Label htmlFor="amount">Amount</Label>
              <Input
                id="amount"
                type="number"
                step="0.01"
                min="0.01"
                {...register("amount", { valueAsNumber: true })}
                className={cn(errors.amount && "border-destructive")}
              />
              {errors.amount && (
                <p className="text-xs text-destructive">{errors.amount.message}</p>
              )}
            </div>

            <div className="w-24 space-y-1.5">
              <Label htmlFor="currency">Currency</Label>
              <Input
                id="currency"
                {...register("currency")}
                placeholder="EUR"
                className={cn(errors.currency && "border-destructive")}
              />
              {errors.currency && (
                <p className="text-xs text-destructive">{errors.currency.message}</p>
              )}
            </div>
          </div>

          <Button type="submit" className="w-full" disabled={isLoading}>
            {isLoading ? (
              <>
                <Loader2 className="h-4 w-4 animate-spin" />
                Creating…
              </>
            ) : (
              "Create payment intent"
            )}
          </Button>
        </form>
      </main>
    </div>
  );
}
