import { useState } from "react";
import { useNavigate, useParams, useSearchParams } from "react-router-dom";
import { GuestCheckoutForm } from "@nebula-ltd/pok-payments-js/react";
import type { PaymentErrorResponse } from "@nebula-ltd/pok-payments-js";
import { CreditCard, Loader2, CheckCircle2, AlertCircle } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import {
  useGetPaymentClientTokenQuery,
  useGetPaymentCapabilitiesQuery,
  useConfirmCardPaymentMutation,
} from "../paymentsApi";
import { PaymentStatus } from "../payment.types";

function CheckoutForm({
  paymentId,
  orderId,
  pokEnvironment,
  amount,
}: { paymentId: string; orderId: string; pokEnvironment: "staging" | "production"; amount?: string | null }) {
  const navigate = useNavigate();
  const [confirmCardPayment] = useConfirmCardPaymentMutation();
  const [succeeded, setSucceeded] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [errorMsg,  setErrorMsg]  = useState<string | null>(null);

  async function handleSuccess() {
    // The widget's own onSuccess is UX only — never the source of truth (ADR-0001: a webhook,
    // and by extension a client-side callback, is a trigger, not a fact). Re-fetch the real
    // status from POK server-side before showing "authorised" — a 200 response here does not by
    // itself mean the deposit cleared; ConfirmCardPaymentCommand can legitimately return the
    // payment still Pending if POK hasn't finished authorizing server-side yet.
    setConfirming(true);
    try {
      const result = await confirmCardPayment(paymentId).unwrap();
      if (result.status === PaymentStatus.Captured || result.status === PaymentStatus.Paid) {
        setSucceeded(true);
      } else {
        setErrorMsg("Your card was submitted, but the payment hasn't been confirmed yet. Refresh in a moment to check again.");
      }
    } catch {
      setErrorMsg("Payment completed, but we couldn't confirm it yet. Refresh in a moment.");
    } finally {
      setConfirming(false);
    }
  }

  if (succeeded) {
    return (
      <div className="flex flex-col items-center gap-3 py-8 text-center">
        <CheckCircle2 className="h-12 w-12 text-green-500" />
        <h2 className="text-lg font-semibold">Deposit authorised</h2>
        <p className="text-sm text-muted-foreground max-w-xs">
          Your card has been authorised. The studio will capture the deposit before your appointment.
        </p>
        <Button variant="outline" size="sm" onClick={() => navigate("/book")}>
          Back to booking
        </Button>
      </div>
    );
  }

  if (confirming) {
    return (
      <div className="flex items-center justify-center gap-2 py-8 text-muted-foreground">
        <Loader2 className="h-5 w-5 animate-spin" />
        <span className="text-sm">Confirming payment…</span>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {amount && (
        <p className="text-sm text-muted-foreground">
          You are authorising a deposit of{" "}
          <span className="font-semibold text-foreground">{amount}</span>.
          Your card will not be charged until the studio confirms your appointment.
        </p>
      )}
      <GuestCheckoutForm
        orderId={orderId}
        onSuccess={() => void handleSuccess()}
        onError={(error: PaymentErrorResponse) => setErrorMsg(error.message ?? "Payment failed. Please try again.")}
        options={{ env: pokEnvironment, locale: "en", countrySelect: "modal" }}
      />
      {errorMsg && (
        <div className="flex items-start gap-2 rounded-md border border-destructive/50 bg-destructive/5 px-3 py-2">
          <AlertCircle className="h-4 w-4 text-destructive mt-0.5 shrink-0" />
          <p className="text-sm text-destructive-text">{errorMsg}</p>
        </div>
      )}
      <p className="text-xs text-center text-muted-foreground">
        Secured by POK. Your card details are never shared with the studio.
      </p>
    </div>
  );
}

export function DepositCheckoutPage() {
  useDocumentMeta({ title: "Deposit Payment — TattooOS", canonical: "/pay" });

  const { paymentId }  = useParams<{ paymentId: string }>();
  const navigate        = useNavigate();
  const [searchParams] = useSearchParams();
  const redirectStatus = searchParams.get("status");
  const amount         = searchParams.get("amount");

  const { data, isLoading, isError } = useGetPaymentClientTokenQuery(paymentId!, {
    skip: !paymentId || redirectStatus === "complete",
  });
  const { data: capabilities } = useGetPaymentCapabilitiesQuery(undefined, {
    skip: redirectStatus === "complete",
  });
  // pokEnvironment must be present whenever the backend reports card payments available — if it's
  // ever not (e.g. a stale cached response), treat cards as unavailable rather than guess an env.
  const pokEnvironment = capabilities?.pokEnvironment;
  const cardPaymentsAvailable = capabilities?.cardPaymentsAvailable === true && !!pokEnvironment;

  if (redirectStatus === "complete") {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center p-4">
        <Card className="w-full max-w-md">
          <CardContent className="py-8">
            <div className="flex flex-col items-center gap-3 text-center">
              <CheckCircle2 className="h-12 w-12 text-green-500" />
              <h2 className="text-lg font-semibold">Deposit authorised</h2>
              <p className="text-sm text-muted-foreground max-w-xs">
                Your card has been authorised. The studio will capture the deposit before your appointment.
              </p>
              <Button variant="outline" size="sm" onClick={() => navigate("/book")}>
                Back to booking
              </Button>
            </div>
          </CardContent>
        </Card>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <CreditCard className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Deposit payment</span>
      </header>

      <main className="max-w-md mx-auto px-4 py-8">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Authorise deposit</CardTitle>
          </CardHeader>
          <CardContent>
            {isLoading && (
              <div className="flex items-center justify-center gap-2 py-8 text-muted-foreground">
                <Loader2 className="h-5 w-5 animate-spin" />
                <span className="text-sm">Loading payment details…</span>
              </div>
            )}

            {isError && (
              <div className="flex flex-col items-center gap-3 py-8 text-center">
                <AlertCircle className="h-8 w-8 text-destructive" />
                <p className="text-sm text-destructive-text">
                  Payment not found or you don't have access to it.
                </p>
              </div>
            )}

            {data?.clientToken && !cardPaymentsAvailable && (
              <div className="flex flex-col items-center gap-3 py-8 text-center">
                <AlertCircle className="h-8 w-8 text-destructive" />
                <p className="text-sm text-destructive-text">
                  Card payments are temporarily unavailable for this link. Please contact the studio.
                </p>
              </div>
            )}

            {data?.clientToken && cardPaymentsAvailable && (
              <CheckoutForm
                paymentId={paymentId!}
                orderId={data.clientToken}
                pokEnvironment={pokEnvironment as "staging" | "production"}
                amount={amount}
              />
            )}
          </CardContent>
        </Card>
      </main>
    </div>
  );
}
