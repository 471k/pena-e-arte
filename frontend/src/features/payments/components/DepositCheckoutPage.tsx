import { type FormEvent, useState } from "react";
import { useNavigate, useParams, useSearchParams } from "react-router-dom";
import { loadStripe } from "@stripe/stripe-js";
import { Elements, PaymentElement, useStripe, useElements } from "@stripe/react-stripe-js";
import { CreditCard, Loader2, CheckCircle2, AlertCircle } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useGetPaymentClientSecretQuery } from "../paymentsApi";

// Lazily initialised so Stripe.js (and its iframe) only loads when this page
// actually mounts, not whenever this module is bundled into the app.
let stripePromise: ReturnType<typeof loadStripe> | null = null;
function getStripePromise() {
  stripePromise ??= loadStripe(import.meta.env.VITE_STRIPE_PUBLISHABLE_KEY ?? "");
  return stripePromise;
}

function CheckoutForm({ paymentId, amount }: { paymentId: string; amount?: string | null }) {
  const stripe   = useStripe();
  const elements = useElements();
  const navigate = useNavigate();

  const [isProcessing, setIsProcessing] = useState(false);
  const [succeeded,    setSucceeded]    = useState(false);
  const [errorMsg,     setErrorMsg]     = useState<string | null>(null);

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!stripe || !elements) return;

    setIsProcessing(true);
    setErrorMsg(null);

    const { error } = await stripe.confirmPayment({
      elements,
      confirmParams: {
        return_url: `${import.meta.env.VITE_PUBLIC_URL ?? window.location.origin}/pay/${paymentId}?status=complete`,
      },
      redirect: "if_required",
    });

    if (error) {
      setErrorMsg(error.message ?? "Payment failed. Please try again.");
      setIsProcessing(false);
    } else {
      setSucceeded(true);
      setIsProcessing(false);
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

  return (
    <form onSubmit={handleSubmit} className="space-y-6">
      {amount && (
        <p className="text-sm text-muted-foreground">
          You are authorising a deposit of{" "}
          <span className="font-semibold text-foreground">{amount}</span>.
          Your card will not be charged until the studio confirms your appointment.
        </p>
      )}
      <PaymentElement />
      {errorMsg && (
        <div className="flex items-start gap-2 rounded-md border border-destructive/50 bg-destructive/5 px-3 py-2">
          <AlertCircle className="h-4 w-4 text-destructive mt-0.5 shrink-0" />
          <p className="text-sm text-destructive">{errorMsg}</p>
        </div>
      )}
      <Button type="submit" className="w-full gap-2" disabled={!stripe || isProcessing}>
        {isProcessing ? (
          <>
            <Loader2 className="h-4 w-4 animate-spin" />
            Processing…
          </>
        ) : (
          <>
            <CreditCard className="h-4 w-4" />
            Authorise deposit
          </>
        )}
      </Button>
      <p className="text-xs text-center text-muted-foreground">
        Secured by Stripe. Your card details are never shared with the studio.
      </p>
    </form>
  );
}

export function DepositCheckoutPage() {
  useDocumentMeta({ title: "Deposit Payment — Pena e Artë", canonical: "/pay" });

  const { paymentId }  = useParams<{ paymentId: string }>();
  const navigate        = useNavigate();
  const [searchParams] = useSearchParams();
  const redirectStatus = searchParams.get("status");
  const amount         = searchParams.get("amount");
  const isDark          = document.documentElement.classList.contains("dark");

  const { data, isLoading, isError } = useGetPaymentClientSecretQuery(paymentId!, {
    skip: !paymentId || redirectStatus === "complete",
  });

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
                <p className="text-sm text-destructive">
                  Payment not found or you don't have access to it.
                </p>
              </div>
            )}

            {data?.clientSecret && (
              <Elements
                stripe={getStripePromise()}
                options={{
                  clientSecret: data.clientSecret,
                  appearance:   { theme: isDark ? "night" : "stripe" },
                }}
              >
                <CheckoutForm paymentId={paymentId!} amount={amount} />
              </Elements>
            )}
          </CardContent>
        </Card>
      </main>
    </div>
  );
}
