import { useEffect, useRef, useState } from "react";
import { GuestCheckoutForm, usePOK } from "@nebula-ltd/pok-payments-js/react";
import type { PaymentErrorResponse } from "@nebula-ltd/pok-payments-js";
import { Banknote, CheckCircle2, CreditCard, Loader2, Star } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { cn } from "@/shared/utils/cn";
import {
  useCreateDepositPaymentMutation,
  useDeclareCashDepositMutation,
  useGetPaymentCapabilitiesQuery,
  usePayWithSavedCardMutation,
} from "@/features/payments/paymentsApi";
import type { PayWithSavedCardSetupResponse } from "@/features/payments/payment.types";
import { useGetSavedPaymentMethodsQuery } from "@/features/saved-payment-methods/savedPaymentMethodsApi";
import type { SavedPaymentMethodResponse } from "@/features/saved-payment-methods/savedPaymentMethod.types";
import { RedeemGiftCardField } from "@/features/gift-cards/components/RedeemGiftCardField";

type Tab = "card" | "cash" | "saved";

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
    // this deposit is trusted as authorised — a 200 response alone doesn't mean that; it can
    // legitimately still report Pending if POK hasn't finished authorizing server-side yet.
    setConfirming(true);
    try {
      const result = await createDeposit({ appointmentId }).unwrap();
      if (result.status === "Captured" || result.status === "Paid") {
        onSuccess("card");
      } else {
        onError("Your card was submitted, but the payment hasn't been confirmed yet. Refresh in a moment to check again.");
      }
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

// ── Saved card tab ────────────────────────────────────────────────────────

function SavedCardOption({
  method, selected, onSelect,
}: { method: SavedPaymentMethodResponse; selected: boolean; onSelect: () => void }) {
  return (
    <button
      type="button"
      onClick={onSelect}
      className={cn(
        "w-full flex items-center gap-3 rounded-lg border p-3 text-left transition-colors",
        selected ? "border-primary bg-primary/5" : "border-input hover:bg-muted/40",
      )}
    >
      <CreditCard className="h-4 w-4 shrink-0 text-muted-foreground" />
      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium">
          {method.cardBrand ?? "Card on file"} {method.maskedPan ?? ""}
        </p>
        {method.expiryMonth && method.expiryYear && (
          <p className="text-xs text-muted-foreground">Expires {method.expiryMonth}/{method.expiryYear}</p>
        )}
      </div>
      {method.isDefault && <Star className="h-3.5 w-3.5 shrink-0 text-amber-500" />}
    </button>
  );
}

// This tab's 3DS-challenge flow is the least-verified part of this codebase's POK integration
// (see IPokCardTokenService's own doc comment, backend) — usePOK is a hook, so it must be
// called unconditionally on every render (React's rules of hooks); it's bound to setup?.orderId
// (empty string until a setup response arrives) and payByCardToken is only actually invoked
// once that setup is ready, via the effect below.
function SavedCardTab({
  appointmentId,
  onSuccess,
  onError,
}: Pick<PaymentMethodSelectorProps, "appointmentId" | "onSuccess" | "onError">) {
  const { data: methods, isLoading: isLoadingMethods } = useGetSavedPaymentMethodsQuery();
  const { data: capabilities } = useGetPaymentCapabilitiesQuery();
  const [payWithSavedCard, { isLoading: isSettingUp }] = usePayWithSavedCardMutation();
  const [createDeposit] = useCreateDepositPaymentMutation();

  const [selectedMethodId, setSelectedMethodId] = useState<string | null>(null);
  const [setup, setSetup] = useState<PayWithSavedCardSetupResponse | null>(null);
  const [confirming, setConfirming] = useState(false);

  const pokEnvironment = (capabilities?.pokEnvironment as "staging" | "production" | undefined) ?? "staging";

  // Derived, not effect-driven: defaults to the client's default card (or the first one) until
  // they explicitly pick a different one via SavedCardOption's onSelect.
  const effectiveMethodId = selectedMethodId
    ?? methods?.find((m) => m.isDefault)?.id
    ?? methods?.[0]?.id
    ?? null;

  async function handlePaySuccess() {
    // Same "never trust the client-side callback" discipline as CardCheckoutForm above —
    // re-run the reconciling create/resume call before reporting success.
    setConfirming(true);
    try {
      const result = await createDeposit({ appointmentId }).unwrap();
      if (result.status === "Captured" || result.status === "Paid") {
        onSuccess("card");
      } else {
        onError("Your card was submitted, but the payment hasn't been confirmed yet. Refresh in a moment to check again.");
      }
    } catch {
      onError("Payment completed, but we couldn't confirm it yet. Refresh in a moment.");
    } finally {
      setConfirming(false);
    }
  }

  function handlePokError(err: PaymentErrorResponse) {
    onError(err.message ?? "Card payment failed. Please try again.");
    setSetup(null);
  }

  const { payByCardToken } = usePOK(
    setup?.orderId ?? "",
    () => void handlePaySuccess(),
    handlePokError,
    pokEnvironment,
    "en",
  );

  // Once a setup response with everything payByCardToken needs has arrived, trigger the 3DS
  // challenge — POK's SDK renders its own step-up modal globally; there's no container to mount
  // here (usePOK takes no containerId, unlike AddCardForm/GuestCheckoutForm).
  useEffect(() => {
    if (!setup?.payerAuthSetupReferenceId || !setup.cardTokenId) return;
    payByCardToken({
      creditDebitCard: { id: setup.cardTokenId },
      payerAuthSetupReferenceId: setup.payerAuthSetupReferenceId,
      deviceDataCollection: setup.deviceDataCollection ?? undefined,
    });
    // Only re-run when a genuinely new setup arrives, not on every payByCardToken identity change.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [setup]);

  async function handlePayClick() {
    if (!effectiveMethodId) return;
    const result = await payWithSavedCard({ appointmentId, savedPaymentMethodId: effectiveMethodId });
    if ("error" in result) {
      onError("Could not start payment with this card. Please try again.");
      return;
    }
    if (result.data.status === "Captured" || result.data.status === "Paid") {
      onSuccess("card");
      return;
    }
    if (!result.data.payerAuthSetupReferenceId || !result.data.cardTokenId) {
      onError("Could not start the payment verification step. Please try again.");
      return;
    }
    setSetup(result.data);
  }

  if (isLoadingMethods) {
    return (
      <div className="flex items-center justify-center py-8 text-muted-foreground gap-2">
        <Loader2 className="h-4 w-4 animate-spin" />
        <span className="text-sm">Loading saved cards…</span>
      </div>
    );
  }

  if (confirming || (setup && !confirming)) {
    return (
      <div className="flex items-center justify-center py-8 text-muted-foreground gap-2">
        <Loader2 className="h-4 w-4 animate-spin" />
        <span className="text-sm">
          {confirming ? "Confirming payment…" : "Verifying your card — check for a confirmation prompt…"}
        </span>
      </div>
    );
  }

  return (
    <div className="space-y-3">
      <div className="space-y-2">
        {(methods ?? []).map((method) => (
          <SavedCardOption
            key={method.id}
            method={method}
            selected={method.id === effectiveMethodId}
            onSelect={() => setSelectedMethodId(method.id)}
          />
        ))}
      </div>
      <Button
        className="w-full"
        onClick={() => void handlePayClick()}
        disabled={!effectiveMethodId || isSettingUp}
      >
        {isSettingUp ? (
          <><Loader2 className="h-4 w-4 animate-spin mr-2" />Preparing…</>
        ) : (
          "Pay with this card"
        )}
      </Button>
      <p className="text-xs text-center text-muted-foreground">
        You may be asked to confirm this payment with your bank.
      </p>
    </div>
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
  const { data: savedMethods } = useGetSavedPaymentMethodsQuery();
  const hasSavedMethods = (savedMethods?.length ?? 0) > 0;

  // Derived, not effect-driven: defaults to the saved-card tab once we know the client has one,
  // until they explicitly click a different tab (setTab below) — a client removing their last
  // saved card mid-session doesn't yank them off whichever tab they're already on, since a
  // non-null tab always wins over this default.
  const [tab, setTab] = useState<Tab | null>(null);
  const effectiveTab: Tab = tab ?? (hasSavedMethods ? "saved" : "card");

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
        {hasSavedMethods && (
          <button type="button" className={tabClass(effectiveTab === "saved")} onClick={() => setTab("saved")}>
            <Star className="h-4 w-4" />
            Saved card
          </button>
        )}
        <button type="button" className={tabClass(effectiveTab === "card")} onClick={() => setTab("card")}>
          <CreditCard className="h-4 w-4" />
          {hasSavedMethods ? "New card" : "Card"}
        </button>
        <button type="button" className={tabClass(effectiveTab === "cash")} onClick={() => setTab("cash")}>
          <Banknote className="h-4 w-4" />
          Cash
        </button>
      </div>

      {effectiveTab === "saved" && (
        <SavedCardTab appointmentId={appointmentId} onSuccess={onSuccess} onError={onError} />
      )}

      {effectiveTab === "card" && (
        <CardTab appointmentId={appointmentId} onSuccess={onSuccess} onError={onError} />
      )}

      {effectiveTab === "cash" && (
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
