import { useEffect, useRef, useState } from "react";
import { GuestCheckoutForm } from "@nebula-ltd/pok-payments-js/react";
import type { PaymentErrorResponse } from "@nebula-ltd/pok-payments-js";
import { Banknote, CheckCircle2, CreditCard, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { cn } from "@/shared/utils/cn";
import {
  useCreateDepositPaymentMutation,
  useDeclareCashDepositMutation,
  useGetPaymentCapabilitiesQuery,
} from "@/features/payments/paymentsApi";
import { RedeemGiftCardField } from "@/features/gift-cards/components/RedeemGiftCardField";

type Tab = "card" | "cash";

interface PaymentMethodSelectorProps {
  appointmentId: string;
  amount:        number;
  onSuccess:     (method: "card" | "cash") => void;
  onError:       (message: string) => void;
}

// ── Card tab ──────────────────────────────────────────────────────────────

function CardCheckoutForm({
  appointmentId,
  orderId,
  pokEnvironment,
  onSuccess,
  onError,
}: Pick<PaymentMethodSelectorProps, "appointmentId" | "onSuccess" | "onError">
  & { orderId: string; pokEnvironment: "staging" | "production" }) {
  const [createDeposit] = useCreateDepositPaymentMutation();
  const [confirming, setConfirming] = useState(false);

  async function handleSuccess() {
    // The widget's own onSuccess is UX only — never the source of truth (ADR-0001: a webhook,
    // and by extension a client-side callback, is a trigger, not a fact). Re-run the same
    // create/resume call, which reconciles against POK's real order status server-side before
    // this deposit is trusted as authorised.
    setConfirming(true);
    try {
      await createDeposit({ appointmentId }).unwrap();
      onSuccess("card");
    } catch {
      onError("Payment completed, but we couldn't confirm it yet. Refresh in a moment.");
    } finally {
      setConfirming(false);
    }
  }

  function handleError(err: PaymentErrorResponse) {
    onError(err.message ?? "Card payment failed. Please try again.");
  }

  return (
    <div className="space-y-3">
      {confirming
        ? (
          <div className="flex items-center justify-center py-8 text-muted-foreground gap-2">
            <Loader2 className="h-4 w-4 animate-spin" />
            <span className="text-sm">Confirming payment…</span>
          </div>
        )
        : (
          <GuestCheckoutForm
            orderId={orderId}
            onSuccess={() => void handleSuccess()}
            onError={handleError}
            options={{ env: pokEnvironment, locale: "en", countrySelect: "modal" }}
          />
        )}
      <p className="text-xs text-center text-muted-foreground">
        Your card is authorised now and charged when the studio confirms your session.
      </p>
    </div>
  );
}

function CardTab({
  appointmentId,
  onSuccess,
  onError,
}: Pick<PaymentMethodSelectorProps, "appointmentId" | "onSuccess" | "onError">) {
  const [createDeposit, { data, isLoading, isError, error }] = useCreateDepositPaymentMutation();
  const { data: capabilities, isLoading: isLoadingCapabilities } = useGetPaymentCapabilitiesQuery();
  const requested = useRef(false);
  // pokEnvironment must be present whenever the backend reports card payments available — if it's
  // ever not (e.g. a stale cached response), treat cards as unavailable rather than guess an env.
  const pokEnvironment = capabilities?.pokEnvironment;
  const cardPaymentsAvailable = capabilities?.cardPaymentsAvailable === true && !!pokEnvironment;

  // Create (or resume) the deposit intent once when the card tab opens — but only once we
  // know card payments are actually available; otherwise this would race capabilities and
  // sometimes fire the request before the "unavailable" response comes back.
  useEffect(() => {
    if (isLoadingCapabilities || !cardPaymentsAvailable || requested.current) return;
    requested.current = true;
    void createDeposit({ appointmentId });
  }, [appointmentId, createDeposit, cardPaymentsAvailable, isLoadingCapabilities]);

  if (isLoadingCapabilities) {
    return (
      <div className="flex items-center justify-center py-8 text-muted-foreground gap-2">
        <Loader2 className="h-4 w-4 animate-spin" />
        <span className="text-sm">Preparing payment form…</span>
      </div>
    );
  }

  if (!cardPaymentsAvailable) {
    return (
      <p className="text-sm text-destructive-text py-4 text-center">
        Card payments are temporarily unavailable. Use the Cash option below,
        or contact the studio directly.
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
      <p className="text-sm text-destructive-text py-4 text-center">
        {err?.data?.message ?? "Could not prepare the card payment. Please try again."}
      </p>
    );
  }

  // The backend reconciles with POK — the deposit may already be settled
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
    <CardCheckoutForm
      appointmentId={appointmentId}
      orderId={data.clientToken}
      pokEnvironment={pokEnvironment as "staging" | "production"}
      onSuccess={onSuccess}
      onError={onError}
    />
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
      {amount > 0 && <RedeemGiftCardField appointmentId={appointmentId} amount={amount} />}

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
