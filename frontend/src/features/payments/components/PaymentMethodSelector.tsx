import { useEffect, useRef, useState } from "react";
import { loadStripe } from "@stripe/stripe-js";
import {
  Elements,
  PaymentElement,
  useStripe,
  useElements,
} from "@stripe/react-stripe-js";
import { Banknote, CheckCircle2, CreditCard, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { cn } from "@/shared/utils/cn";
import {
  useCreateDepositPaymentMutation,
  useDeclareCashDepositMutation,
} from "@/features/payments/paymentsApi";

const stripeKey: string | undefined = import.meta.env.VITE_STRIPE_PUBLISHABLE_KEY;
// Missing key (e.g. .env.local not set up or dev server started before it existed)
// must not crash the selector — the card tab explains instead.
const stripePromise = stripeKey ? loadStripe(stripeKey) : null;

type Tab = "card" | "cash";

interface PaymentMethodSelectorProps {
  appointmentId: string;
  amount:        number;
  onSuccess:     (method: "card" | "cash") => void;
  onError:       (message: string) => void;
}

// ── Card tab ──────────────────────────────────────────────────────────────

function CardCheckoutForm({
  onSuccess,
  onError,
}: Pick<PaymentMethodSelectorProps, "onSuccess" | "onError">) {
  const stripe      = useStripe();
  const elements    = useElements();
  const [busy, setBusy] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!stripe || !elements) return;
    setBusy(true);
    const { error } = await stripe.confirmPayment({
      elements,
      confirmParams: {
        return_url: `${window.location.origin}/booking/success`,
      },
      redirect: "if_required",
    });
    setBusy(false);
    if (error) onError(error.message ?? "Card payment failed.");
    else        onSuccess("card");
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <PaymentElement
        onLoadError={() =>
          onError(
            "The card form failed to load. Check your connection or disable ad/tracking blockers for this site, then try again.",
          )
        }
      />
      <Button type="submit" className="w-full" disabled={busy || !stripe}>
        {busy
          ? <><Loader2 className="h-4 w-4 animate-spin mr-2" />Processing…</>
          : "Authorise deposit"}
      </Button>
      <p className="text-xs text-center text-muted-foreground">
        Your card is authorised now and charged when the studio confirms your session.
      </p>
    </form>
  );
}

function CardTab({
  appointmentId,
  onSuccess,
  onError,
}: Pick<PaymentMethodSelectorProps, "appointmentId" | "onSuccess" | "onError">) {
  const [createDeposit, { data, isLoading, isError, error }] = useCreateDepositPaymentMutation();
  const requested = useRef(false);

  // Create (or resume) the deposit intent once when the card tab opens.
  // The backend is idempotent — an unauthorized intent for this appointment is reused.
  useEffect(() => {
    if (!stripePromise || requested.current) return;
    requested.current = true;
    void createDeposit({ appointmentId });
  }, [appointmentId, createDeposit]);

  if (!stripePromise) {
    return (
      <p className="text-sm text-destructive py-4 text-center">
        Card payments are not configured (missing Stripe publishable key).
        Use the Cash option or contact the studio.
      </p>
    );
  }

  if (isLoading || (!data && !isError)) {
    return (
      <div className="flex items-center justify-center py-8 text-muted-foreground gap-2">
        <Loader2 className="h-4 w-4 animate-spin" />
        <span className="text-sm">Preparing payment form…</span>
      </div>
    );
  }

  if (isError || !data) {
    const err = error as { data?: { message?: string } } | undefined;
    return (
      <p className="text-sm text-destructive py-4 text-center">
        {err?.data?.message ?? "Could not prepare the card payment. Please try again."}
      </p>
    );
  }

  // The backend reconciles with Stripe — the deposit may already be settled
  // (e.g. authorized earlier in another tab, or a webhook arrived late).
  if (data.status === "Captured" || data.status === "Paid") {
    return (
      <div className="rounded-lg border border-input bg-muted/50 p-4 text-sm space-y-1">
        <p className="font-medium flex items-center gap-2 text-green-600 dark:text-green-400">
          <CheckCircle2 className="h-4 w-4" />
          {data.status === "Paid" ? "Deposit already paid" : "Deposit already authorised"}
        </p>
        <p className="text-muted-foreground">
          {data.status === "Paid"
            ? "This deposit has been paid — nothing more to do."
            : "Your card is authorised and will be charged when the studio confirms your session."}
        </p>
      </div>
    );
  }

  return (
    <Elements
      stripe={stripePromise}
      options={{
        clientSecret: data.clientSecret,
        appearance:   { theme: "stripe" },
      }}
    >
      <CardCheckoutForm onSuccess={onSuccess} onError={onError} />
    </Elements>
  );
}

// ── Cash tab ──────────────────────────────────────────────────────────────

function CashInfoPanel({
  appointmentId,
  amount,
  onSuccess,
  onError,
}: Pick<PaymentMethodSelectorProps, "appointmentId" | "amount" | "onSuccess" | "onError">) {
  const [declareCash, { isLoading }] = useDeclareCashDepositMutation();

  async function handleSelect() {
    try {
      await declareCash({ appointmentId }).unwrap();
      onSuccess("cash");
    } catch {
      onError("Could not register cash payment. Please try again.");
    }
  }

  return (
    <div className="space-y-4">
      <div className="rounded-lg border border-input bg-muted/50 p-4 space-y-2 text-sm">
        <p className="font-medium">Pay at the studio</p>
        <p className="text-muted-foreground">
          Your deposit of{" "}
          <span className="font-medium text-foreground">
            €{amount.toFixed(2)}
          </span>{" "}
          will be collected in cash when you arrive.
          Your booking will be held as pending until the studio confirms receipt.
        </p>
        <p className="text-muted-foreground text-xs">
          The studio may contact you to confirm your appointment before your visit.
        </p>
      </div>
      <Button className="w-full" onClick={handleSelect} disabled={isLoading}>
        {isLoading
          ? <><Loader2 className="h-4 w-4 animate-spin mr-2" />Saving…</>
          : "Confirm — I'll pay cash at the studio"}
      </Button>
    </div>
  );
}

// ── Main ──────────────────────────────────────────────────────────────────

export function PaymentMethodSelector({
  appointmentId,
  amount,
  onSuccess,
  onError,
}: PaymentMethodSelectorProps) {
  const [tab, setTab] = useState<Tab>("card");

  const tabClass = (active: boolean) =>
    cn(
      "flex items-center gap-2 flex-1 justify-center py-2.5 rounded-md text-sm font-medium transition-colors",
      active
        ? "bg-background text-foreground shadow-sm"
        : "text-muted-foreground hover:text-foreground"
    );

  return (
    <div className="space-y-4">
      {/* Tab bar */}
      <div className="flex gap-1 rounded-lg bg-muted p-1">
        <button type="button" className={tabClass(tab === "card")} onClick={() => setTab("card")}>
          <CreditCard className="h-4 w-4" />
          Card
        </button>
        <button type="button" className={tabClass(tab === "cash")} onClick={() => setTab("cash")}>
          <Banknote className="h-4 w-4" />
          Cash
        </button>
      </div>

      {tab === "card" && (
        <CardTab appointmentId={appointmentId} onSuccess={onSuccess} onError={onError} />
      )}

      {tab === "cash" && (
        <CashInfoPanel
          appointmentId={appointmentId}
          amount={amount}
          onSuccess={onSuccess}
          onError={onError}
        />
      )}
    </div>
  );
}
