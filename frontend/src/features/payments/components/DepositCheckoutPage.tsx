import { useState } from "react";
import { useNavigate, useParams, useSearchParams } from "react-router-dom";
import { GuestCheckoutForm } from "@nebula-ltd/pok-payments-js/react";
import type { PaymentErrorResponse } from "@nebula-ltd/pok-payments-js";
import { CreditCard, Loader2, CheckCircle2, AlertCircle } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { lekReferenceApplies } from "@/shared/utils/formatCurrency";
import { Money } from "@/shared/components/Money";
import type { FrozenReference, MoneyStudio } from "@/shared/hooks/useEurAllRate";
import {
  useGetPaymentClientTokenQuery,
  useGetPaymentCapabilitiesQuery,
  useConfirmCardPaymentMutation,
} from "../paymentsApi";
import { PaymentStatus } from "../payment.types";

// Spec D6: POK's own card page shows euros only; this is the line our page adds right before paying.
export const CARD_CHARGED_IN_EUR_TEXT =
  "Your card is charged in euros. If your card is in lek, your bank converts it at its own rate, which can differ slightly.";

// Card deposits are charged when the client pays (go-live decision C-4, 2026-10-22).
const DEPOSIT_PAID_TEXT =
  "Your card has been charged. If you cancel, the studio's cancellation policy decides how much of the deposit is refunded.";

function CheckoutForm({
  paymentId,
  orderId,
  pokEnvironment,
  amount,
  currency,
  studio,
  frozen,
}: {
  paymentId: string; orderId: string; pokEnvironment: "staging" | "production";
  amount: number; currency: string; studio: MoneyStudio; frozen: FrozenReference;
}) {
  const navigate = useNavigate();
  const [confirmCardPayment] = useConfirmCardPaymentMutation();
  // "paid": charged now (every deposit since C-4, 2026-10-22). "held": a hold from before then.
  const [succeeded, setSucceeded] = useState<"paid" | "held" | null>(null);
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
      if (result.status === PaymentStatus.Paid) {
        setSucceeded("paid");
      } else if (result.status === PaymentStatus.Captured) {
        setSucceeded("held");
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
        <h2 className="text-lg font-semibold">{succeeded === "paid" ? "Deposit paid" : "Deposit authorised"}</h2>
        <p className="text-sm text-muted-foreground max-w-xs">
          {succeeded === "paid"
            ? DEPOSIT_PAID_TEXT
            : "Your card has been authorised. The studio charges the deposit, usually at your appointment; its cancellation policy applies if you cancel late or miss it."}
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
      <p className="text-sm text-muted-foreground">
        You are paying a deposit of{" "}
        <Money amount={amount} currency={currency} studio={studio} frozen={frozen}
          className="font-semibold text-foreground" />.
        Your card is charged now. If you cancel, the studio's cancellation policy decides how much is refunded.
      </p>
      {(lekReferenceApplies(studio.countryCode, currency) || frozen.referenceAmount != null) && (
        <p className="text-sm text-muted-foreground">{CARD_CHARGED_IN_EUR_TEXT}</p>
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
              <h2 className="text-lg font-semibold">Deposit paid</h2>
              <p className="text-sm text-muted-foreground max-w-xs">
                {DEPOSIT_PAID_TEXT}
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
            <CardTitle className="text-base">Pay deposit</CardTitle>
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
                amount={data.amount}
                currency={data.currency}
                studio={{ countryCode: data.studioCountryCode, currency: data.currency }}
                frozen={{ referenceAmount: data.referenceAmount, referenceCurrency: data.referenceCurrency }}
              />
            )}
          </CardContent>
        </Card>
      </main>
    </div>
  );
}
