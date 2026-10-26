import { useEffect, useRef, useState } from "react";
import { GuestCheckoutForm, usePOK } from "@nebula-ltd/pok-payments-js/react";
import type { PaymentErrorResponse } from "@nebula-ltd/pok-payments-js";
import { AlertCircle, Banknote, CheckCircle2, CreditCard, Loader2, Star } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { cn } from "@/shared/utils/cn";
import { Money } from "@/shared/components/Money";
import {
  useCreateDepositPaymentMutation,
  useDeclareCashDepositMutation,
  useGetPaymentCapabilitiesQuery,
  usePayWithSavedCardMutation,
} from "@/features/payments/paymentsApi";
import { CardUnavailableReasons, type PayWithSavedCardSetupResponse } from "@/features/payments/payment.types";
import { describeCardPaymentError } from "@/features/payments/cardPaymentError";
import { useGetSavedPaymentMethodsQuery } from "@/features/saved-payment-methods/savedPaymentMethodsApi";
import type { SavedPaymentMethodResponse } from "@/features/saved-payment-methods/savedPaymentMethod.types";
import { RedeemGiftCardField } from "@/features/gift-cards/components/RedeemGiftCardField";

type Tab = "card" | "cash" | "saved";

interface PaymentMethodSelectorProps {
  appointmentId: string;
  amount:        number;
  /** The studio's country: with its currency it decides whether lek is shown next to euro (AL + EUR). */
  countryCode?:  string | null;
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
  // After a failed attempt POK's widget stays on its own "Error!" screen with no way back, so we
  // replace it with our message + "Try again", which remounts a fresh widget (new key) on the same
  // order — a failed attempt leaves the POK order unpaid and payable.
  const [failure, setFailure] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);

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
    const message = describeCardPaymentError(err.message);
    setFailure(message);
    onError(message);
  }

  async function handleRetry() {
    // Re-check with the backend first: if POK did authorise despite reporting an error, the
    // reconciling create/resume call says so and we finish instead of offering a second payment.
    setConfirming(true);
    try {
      const result = await createDeposit({ appointmentId }).unwrap();
      if (result.status === "Captured" || result.status === "Paid") {
        onSuccess("card");
        return;
      }
    } catch {
      // Couldn't re-check (e.g. offline for a moment) — still offer a fresh form.
    } finally {
      setConfirming(false);
    }
    setFailure(null);
    setAttempt((n) => n + 1);
  }

  if (confirming) {
    return (
      <div className="flex items-center justify-center py-8 text-muted-foreground gap-2">
        <Loader2 className="h-4 w-4 animate-spin" />
        <span className="text-sm">Confirming payment…</span>
      </div>
    );
  }

  if (failure) {
    return (
      <div role="alert" className="rounded-lg border border-input bg-muted/50 p-4 space-y-3 text-sm">
        <p className="font-medium flex items-start gap-2 text-destructive-text">
          <AlertCircle className="h-4 w-4 mt-0.5 shrink-0" />
          {failure}
        </p>
        <p className="text-muted-foreground">Nothing was charged to your card.</p>
        <Button className="w-full" onClick={() => void handleRetry()}>Try again</Button>
      </div>
    );
  }

  return (
    <div className="space-y-3">
      <GuestCheckoutForm
        key={attempt}
        orderId={orderId}
        onSuccess={() => void handleSuccess()}
        onError={handleError}
        options={{ env: pokEnvironment, locale: "en", countrySelect: "modal" }}
      />
      <p className="text-xs text-center text-muted-foreground">
        Your card is charged now. If you cancel, the studio's cancellation policy decides how much is refunded.
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
            : "Your card is authorised and will be charged by the studio, usually at your appointment. The studio's cancellation policy applies if you cancel late or miss it."}
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

// usePOK (POK SDK 2.0.1) loads its order once, on mount, and payByCardToken sends that order's
// amount and currency to POK's check-3ds-enrollment. Mounted with an empty order id it never
// loads the real order and POK answers 400, so this component only mounts once the setup (and its
// order id) exists, and starts the 3DS flow only after the order has loaded.
function SavedCardVerification({
  setup,
  pokEnvironment,
  onSuccess,
  onError,
}: {
  setup: PayWithSavedCardSetupResponse & { orderId: string; cardTokenId: string; payerAuthSetupReferenceId: string };
  pokEnvironment: "staging" | "production";
  onSuccess: () => void;
  onError: (err: PaymentErrorResponse) => void;
}) {
  const { order, payByCardToken } = usePOK(setup.orderId, onSuccess, onError, pokEnvironment, "en");
  const started = useRef(false);

  // POK's SDK renders its own step-up modal globally; there's no container to mount here.
  useEffect(() => {
    if (started.current || !order?.currencyCode) return;
    started.current = true;
    payByCardToken({
      creditDebitCard: { id: setup.cardTokenId },
      payerAuthSetupReferenceId: setup.payerAuthSetupReferenceId,
      deviceDataCollection: setup.deviceDataCollection ?? undefined,
    });
  }, [order, payByCardToken, setup]);

  return (
    <div className="flex items-center justify-center py-8 text-muted-foreground gap-2">
      <Loader2 className="h-4 w-4 animate-spin" />
      <span className="text-sm">Verifying your card — check for a confirmation prompt…</span>
    </div>
  );
}

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
    onError(describeCardPaymentError(err.message));
    setSetup(null);
  }

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
    if (!result.data.payerAuthSetupReferenceId || !result.data.cardTokenId || !result.data.orderId) {
      onError("Could not start the payment verification step. Please try again.");
      return;
    }
    setSetup(result.data);
  }

  const readySetup = setup?.orderId && setup.cardTokenId && setup.payerAuthSetupReferenceId
    ? { ...setup, orderId: setup.orderId, cardTokenId: setup.cardTokenId, payerAuthSetupReferenceId: setup.payerAuthSetupReferenceId }
    : null;

  if (isLoadingMethods) {
    return (
      <div className="flex items-center justify-center py-8 text-muted-foreground gap-2">
        <Loader2 className="h-4 w-4 animate-spin" />
        <span className="text-sm">Loading saved cards…</span>
      </div>
    );
  }

  if (confirming) {
    return (
      <div className="flex items-center justify-center py-8 text-muted-foreground gap-2">
        <Loader2 className="h-4 w-4 animate-spin" />
        <span className="text-sm">Confirming payment…</span>
      </div>
    );
  }

  if (readySetup) {
    return (
      <SavedCardVerification
        key={readySetup.orderId}
        setup={readySetup}
        pokEnvironment={pokEnvironment}
        onSuccess={() => void handlePaySuccess()}
        onError={handlePokError}
      />
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
  countryCode,
  onSuccess,
  onError,
}: Pick<PaymentMethodSelectorProps, "appointmentId" | "amount" | "countryCode" | "onSuccess" | "onError">) {
  const [declareCash, { isLoading }] = useDeclareCashDepositMutation();
  const { data: capabilities } = useGetPaymentCapabilitiesQuery();

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
          {capabilities?.currency ? (
            <>
              Your deposit of{" "}
              <span className="font-medium text-foreground">
                <Money amount={amount} currency={capabilities.currency}
                  studio={{ countryCode, currency: capabilities.currency }} />
              </span>{" "}
              will be collected in cash when you arrive.
            </>
          ) : (
            // Capabilities still loading — never show the bare number (G-2).
            "Your deposit will be collected in cash when you arrive."
          )}
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
  countryCode,
  onSuccess,
  onError,
}: PaymentMethodSelectorProps) {
  const { data: capabilities } = useGetPaymentCapabilitiesQuery();
  // Saved cards are switched off platform-wide until C-9 (Payments:SavedCardsEnabled); while off,
  // the saved-card list isn't even fetched and the card tab is just "Card".
  const savedCardsAvailable = capabilities?.savedCardsAvailable === true;
  const { data: savedMethods, isError: isSavedMethodsError } =
    useGetSavedPaymentMethodsQuery(undefined, { skip: !savedCardsAvailable });
  const hasSavedMethods = savedCardsAvailable && (savedMethods?.length ?? 0) > 0;
  // The saved-card list is only fetched once capabilities say saved cards are on, so it arrives
  // later than before. Until it does, open no tab by default: the Card tab creates a deposit order
  // as soon as it mounts, and the Saved card tab may be the one the client lands on.
  const choosingDefaultTab = savedCardsAvailable && savedMethods === undefined && !isSavedMethodsError;
  // A currency the connected provider can't take at all is a studio-level fact, unlike the
  // generic "not connected"/"disabled" reasons — card (new or saved) is never coming back for
  // this appointment, so both card tabs are hidden rather than shown-then-erroring.
  const cardCurrencyUnsupported =
    capabilities?.cardUnavailableReason === CardUnavailableReasons.ProviderUnsupportedCurrency;

  // Derived, not effect-driven: defaults to the saved-card tab once we know the client has one,
  // until they explicitly click a different tab (setTab below) — a client removing their last
  // saved card mid-session doesn't yank them off whichever tab they're already on, since a
  // non-null tab always wins over this default.
  const [tab, setTab] = useState<Tab | null>(null);
  const effectiveTab: Tab = cardCurrencyUnsupported ? "cash" : tab ?? (hasSavedMethods ? "saved" : "card");

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

      {cardCurrencyUnsupported ? (
        <p className="text-sm text-muted-foreground">
          Card payments aren't available in {capabilities?.currency} for this studio yet — you can
          pay the deposit in cash.
        </p>
      ) : (
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
      )}

      {effectiveTab === "saved" && hasSavedMethods && (
        <SavedCardTab appointmentId={appointmentId} onSuccess={onSuccess} onError={onError} />
      )}

      {effectiveTab === "card" && tab === null && choosingDefaultTab && (
        <div className="flex items-center justify-center py-8 text-muted-foreground gap-2">
          <Loader2 className="h-4 w-4 animate-spin" />
          <span className="text-sm">Preparing payment form…</span>
        </div>
      )}

      {effectiveTab === "card" && !(tab === null && choosingDefaultTab) && (
        <CardTab appointmentId={appointmentId} onSuccess={onSuccess} onError={onError} />
      )}

      {effectiveTab === "cash" && (
        <CashInfoPanel
          appointmentId={appointmentId}
          amount={amount}
          countryCode={countryCode}
          onSuccess={onSuccess}
          onError={onError}
        />
      )}
    </div>
  );
}
