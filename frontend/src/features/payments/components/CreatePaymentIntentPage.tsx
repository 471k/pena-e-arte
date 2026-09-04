import { useEffect, useMemo, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import {
  ArrowLeft,
  Banknote,
  CreditCard,
  Loader2,
  Copy,
  Check,
  ExternalLink,
  Search,
  User,
  CalendarDays,
  ChevronRight,
  AlertCircle,
  CheckCircle2,
} from "lucide-react";
import { Button }                          from "@/shared/components/ui/button";
import { Card, CardContent }               from "@/shared/components/ui/card";
import { Input }                           from "@/shared/components/ui/input";
import { Badge }                           from "@/shared/components/ui/badge";
import { cn }                              from "@/shared/utils/cn";
import { useGetAppointmentsQuery }         from "@/features/appointments/appointmentsApi";
import { useGetClientsQuery }              from "@/features/clients/clientsApi";
import {
  useCreatePaymentIntentMutation,
  useDeclareCashDepositMutation,
  useGetPaymentCapabilitiesQuery,
}                                          from "../paymentsApi";
import type { PaymentIntentResponse, PaymentResponse } from "../payment.types";
import { SessionSplitsEditor }             from "./SessionSplitsEditor";
import type { AppointmentResponse }        from "@/features/appointments/appointment.types";

// ── helpers ───────────────────────────────────────────────────────────────────

function fmtDate(iso: string) {
  return new Date(iso).toLocaleDateString("en-GB", {
    weekday: "short", day: "numeric", month: "short", year: "numeric",
  });
}

function fmtTime(iso: string) {
  return new Date(iso).toLocaleTimeString("en-GB", {
    hour: "2-digit", minute: "2-digit",
  });
}

function fmtCurrency(amount: number) {
  return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(amount);
}

// ── CheckoutLinkPanel ─────────────────────────────────────────────────────────

function CheckoutLinkPanel({
  result,
  appointmentId,
  clientName,
  appointmentDate,
  amount,
}: {
  result:          PaymentIntentResponse;
  appointmentId:   string;
  clientName:      string;
  appointmentDate: string;
  amount:          number;
}) {
  const navigate       = useNavigate();
  const [copied, setCopied] = useState(false);
  const checkoutUrl    = `${window.location.origin}/pay/${result.paymentId}?amount=${amount.toFixed(2)}+EUR`;

  async function copyLink() {
    await navigator.clipboard.writeText(checkoutUrl);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }

  return (
    <div className="space-y-4">
      <div className="rounded-md border border-green-200 bg-green-50 dark:border-green-800 dark:bg-green-950/30 px-4 py-3">
        <p className="text-sm font-medium text-green-800 dark:text-green-400">
          Card payment intent created
        </p>
        <p className="text-xs text-green-700 dark:text-green-500 mt-0.5">
          {clientName} · {appointmentDate}
        </p>
      </div>

      <div className="space-y-1.5">
        <p className="text-sm font-medium">Client checkout link</p>
        <div className="flex gap-2">
          <Input
            readOnly
            value={checkoutUrl}
            className="font-mono text-xs"
            onClick={(e) => (e.target as HTMLInputElement).select()}
          />
          <Button type="button" variant="outline" size="icon" onClick={copyLink} className="shrink-0">
            {copied
              ? <Check className="h-4 w-4 text-green-500" />
              : <Copy className="h-4 w-4" />}
          </Button>
        </div>
        <p className="text-xs text-muted-foreground">
          The client must be logged in to complete the payment.
        </p>
      </div>

      <SessionSplitsEditor paymentId={result.paymentId} paymentAmount={amount} currentSplits={[]} />

      <div className="flex gap-2">
        <Button variant="outline" className="flex-1 gap-2" onClick={() => window.open(checkoutUrl, "_blank")}>
          <ExternalLink className="h-4 w-4" />
          Preview link
        </Button>
        <Button className="flex-1" onClick={() => navigate(`/payments/${appointmentId}`, { replace: true })}>
          View payment
        </Button>
      </div>
    </div>
  );
}

// ── CashResultPanel ───────────────────────────────────────────────────────────

function CashResultPanel({
  result,
  clientName,
  appointmentDate,
}: {
  result:          PaymentResponse;
  clientName:      string;
  appointmentDate: string;
}) {
  const navigate = useNavigate();

  return (
    <div className="space-y-4">
      <div className="rounded-md border border-green-200 bg-green-50 dark:border-green-800 dark:bg-green-950/30 px-4 py-3 flex items-start gap-3">
        <CheckCircle2 className="h-5 w-5 text-green-600 dark:text-green-400 shrink-0 mt-0.5" />
        <div>
          <p className="text-sm font-medium text-green-800 dark:text-green-400">
            Cash payment recorded
          </p>
          <p className="text-xs text-green-700 dark:text-green-500 mt-0.5">
            {clientName} · {appointmentDate}
          </p>
        </div>
      </div>

      <div className="rounded-md border bg-muted/30 px-4 py-3">
        <p className="text-sm text-muted-foreground">
          The payment is waiting for cash collection. Open the payment detail to confirm receipt once the client pays.
        </p>
      </div>

      <SessionSplitsEditor paymentId={result.id} paymentAmount={result.amount} currentSplits={[]} />

      <Button className="w-full" onClick={() => navigate(`/payments/${result.appointmentId}`, { replace: true })}>
        View payment
      </Button>
    </div>
  );
}

// ── AppointmentPicker ─────────────────────────────────────────────────────────

interface EnrichedAppointment extends AppointmentResponse {
  clientName: string;
}

function AppointmentPicker({
  onSelect,
}: {
  onSelect: (appt: EnrichedAppointment) => void;
}) {
  const [search, setSearch] = useState("");

  const { data: appointments = [], isLoading: loadingAppts } = useGetAppointmentsQuery({});
  const { data: clients      = [], isLoading: loadingClients } = useGetClientsQuery(undefined);

  const clientMap = useMemo(() => {
    const map = new Map<string, string>();
    for (const c of clients) {
      map.set(c.id, `${c.firstName} ${c.lastName}`);
    }
    return map;
  }, [clients]);

  const enriched = useMemo<EnrichedAppointment[]>(() => {
    return appointments
      .filter((a) => a.depositStatus === "Pending")
      .map((a) => ({ ...a, clientName: clientMap.get(a.clientId) ?? "Unknown client" }))
      .sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime());
  }, [appointments, clientMap]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return enriched;
    return enriched.filter(
      (a) =>
        a.clientName.toLowerCase().includes(q) ||
        fmtDate(a.date).toLowerCase().includes(q)
    );
  }, [enriched, search]);

  const loading = loadingAppts || loadingClients;

  return (
    <div className="space-y-3">
      <div className="relative">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
        <Input
          placeholder="Search by client name or date…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="pl-9"
        />
      </div>

      {loading && (
        <div className="flex items-center justify-center gap-2 py-8 text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" />
          <span className="text-sm">Loading appointments…</span>
        </div>
      )}

      {!loading && enriched.length === 0 && (
        <p className="text-center text-sm text-muted-foreground py-8">
          No appointments with a pending deposit.
        </p>
      )}

      {!loading && enriched.length > 0 && filtered.length === 0 && (
        <p className="text-center text-sm text-muted-foreground py-6">
          No appointments match "{search}".
        </p>
      )}

      <div className="space-y-2 max-h-80 overflow-y-auto pr-1">
        {filtered.map((appt) => (
          <button
            key={appt.id}
            type="button"
            onClick={() => onSelect(appt)}
            className={cn(
              "w-full text-left rounded-lg border px-4 py-3 transition-colors",
              "hover:bg-muted/60 hover:border-muted-foreground/30",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            )}
          >
            <div className="flex items-center justify-between gap-3">
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2 mb-1">
                  <User className="h-3.5 w-3.5 text-muted-foreground shrink-0" />
                  <span className="font-medium text-sm truncate">{appt.clientName}</span>
                </div>
                <div className="flex items-center gap-2 text-xs text-muted-foreground">
                  <CalendarDays className="h-3.5 w-3.5 shrink-0" />
                  <span>{fmtDate(appt.date)} · {fmtTime(appt.date)}</span>
                </div>
              </div>
              <div className="flex items-center gap-2 shrink-0">
                <Badge variant="outline" className="text-xs font-semibold">
                  {fmtCurrency(appt.depositAmount)}
                </Badge>
                <ChevronRight className="h-4 w-4 text-muted-foreground" />
              </div>
            </div>
          </button>
        ))}
      </div>
    </div>
  );
}

// ── ConfirmPanel ──────────────────────────────────────────────────────────────

type PaymentMethodChoice = "card" | "cash";

function ConfirmPanel({
  appointment,
  onBack,
  onCardCreated,
  onCashCreated,
}: {
  appointment:   EnrichedAppointment;
  onBack:        () => void;
  onCardCreated: (result: PaymentIntentResponse, amount: number) => void;
  onCashCreated: (result: PaymentResponse, amount: number) => void;
}) {
  const { data: capabilities } = useGetPaymentCapabilitiesQuery();
  const cardPaymentsAvailable = capabilities?.cardPaymentsAvailable !== false;

  const [method, setMethod]         = useState<PaymentMethodChoice>("card");
  const [amount, setAmount]         = useState(
    appointment.depositAmount > 0 ? appointment.depositAmount : undefined as number | undefined
  );
  const [amountError, setAmountError] = useState<string | null>(null);

  const [createIntent,     { isLoading: isLoadingCard, isError: isErrorCard }] = useCreatePaymentIntentMutation();
  const [declareCashDeposit, { isLoading: isLoadingCash, isError: isErrorCash }] = useDeclareCashDepositMutation();

  const isLoading = isLoadingCard || isLoadingCash;
  const noDepositRule = appointment.depositAmount === 0;

  // Default (and force) to the cash flow when card payments aren't available —
  // never let the owner select a method that can't be completed.
  useEffect(() => {
    if (!cardPaymentsAvailable && method === "card") setMethod("cash");
  }, [cardPaymentsAvailable, method]);

  function validate(): boolean {
    if (!amount || amount <= 0) {
      setAmountError("Enter a deposit amount greater than 0.");
      return false;
    }
    setAmountError(null);
    return true;
  }

  async function handleSubmit() {
    if (method === "card" && !validate()) return;

    if (method === "card") {
      const result = await createIntent({
        appointmentId: appointment.id,
        clientId:      appointment.clientId,
        amount:        amount!,
        currency:      "EUR",
      });
      if ("data" in result && result.data) onCardCreated(result.data, amount!);
    } else {
      const result = await declareCashDeposit({ appointmentId: appointment.id });
      if ("data" in result && result.data) onCashCreated(result.data, appointment.depositAmount);
    }
  }

  return (
    <div className="space-y-4">
      <button
        type="button"
        onClick={onBack}
        className="flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground transition-colors"
      >
        <ArrowLeft className="h-3.5 w-3.5" />
        Back to appointments
      </button>

      {/* Appointment summary */}
      <div className="rounded-lg border bg-muted/30 px-4 py-4 space-y-3">
        <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide">
          Selected appointment
        </p>
        <div className="flex items-center gap-2">
          <User className="h-4 w-4 text-muted-foreground" />
          <span className="font-semibold">{appointment.clientName}</span>
        </div>
        <div className="flex items-center gap-2 text-sm text-muted-foreground">
          <CalendarDays className="h-4 w-4" />
          <span>{fmtDate(appointment.date)} · {fmtTime(appointment.date)}</span>
        </div>
      </div>

      {/* Payment method selector */}
      <div className="space-y-1.5">
        <p className="text-sm font-medium">Payment method</p>
        <div className="grid grid-cols-2 gap-2">
          {(["card", "cash"] as PaymentMethodChoice[]).map((m) => {
            const disabled = m === "card" && !cardPaymentsAvailable;
            return (
              <button
                key={m}
                type="button"
                onClick={() => !disabled && setMethod(m)}
                disabled={disabled}
                className={cn(
                  "flex items-center gap-2.5 rounded-lg border px-4 py-3 text-sm font-medium transition-colors",
                  disabled
                    ? "opacity-50 cursor-not-allowed"
                    : method === m
                    ? "border-primary bg-primary/5 text-primary"
                    : "hover:bg-muted/60 hover:border-muted-foreground/30"
                )}
              >
                {m === "card"
                  ? <CreditCard className="h-4 w-4 shrink-0" />
                  : <Banknote    className="h-4 w-4 shrink-0" />}
                {m === "card" ? "Card" : "Cash"}
              </button>
            );
          })}
        </div>
        {!cardPaymentsAvailable && (
          <p className="text-xs text-destructive pt-1">
            Card payments are temporarily unavailable. Use the Cash option below.
          </p>
        )}
        {cardPaymentsAvailable && method === "cash" && (
          <p className="text-xs text-muted-foreground pt-1">
            Records a pending cash payment. You will confirm receipt once collected.
          </p>
        )}
      </div>

      {/* Deposit amount */}
      <div className="space-y-1.5">
        <label htmlFor="amount" className="text-sm font-medium">
          Deposit amount (EUR)
        </label>

        {noDepositRule && (
          <div className="flex items-start gap-2 rounded-md border border-yellow-300/50 bg-yellow-50/10 px-3 py-2">
            <AlertCircle className="h-4 w-4 text-yellow-500 mt-0.5 shrink-0" />
            <p className="text-xs text-yellow-600 dark:text-yellow-400">
              No deposit rule is set for this appointment. Enter the amount manually.
            </p>
          </div>
        )}

        <div className="relative">
          <Input
            id="amount"
            type="number"
            min="0.01"
            step="0.01"
            placeholder="0.00"
            value={amount ?? ""}
            onChange={(e) => {
              setAmount(e.target.value ? parseFloat(e.target.value) : undefined);
              setAmountError(null);
            }}
            className={cn("pr-12", amountError && "border-destructive")}
          />
          <span className="absolute right-3 top-1/2 -translate-y-1/2 text-sm text-muted-foreground pointer-events-none">
            EUR
          </span>
        </div>

        {amountError && (
          <p className="text-xs text-destructive">{amountError}</p>
        )}
        {!noDepositRule && (
          <p className="text-xs text-muted-foreground">
            Pre-filled from the deposit rule. You can adjust if needed.
          </p>
        )}
      </div>

      {(isErrorCard || isErrorCash) && (
        <Card>
          <CardContent className="p-3">
            <p className="text-sm text-destructive">
              Failed to create payment. This appointment may already have an active payment.
            </p>
          </CardContent>
        </Card>
      )}

      <Button className="w-full gap-2" onClick={handleSubmit} disabled={isLoading || !amount}>
        {isLoading ? (
          <>
            <Loader2 className="h-4 w-4 animate-spin" />
            Creating…
          </>
        ) : method === "card" ? (
          <>
            <CreditCard className="h-4 w-4" />
            Create card payment{amount ? ` · ${fmtCurrency(amount)}` : ""}
          </>
        ) : (
          <>
            <Banknote className="h-4 w-4" />
            Record cash payment{amount ? ` · ${fmtCurrency(amount)}` : ""}
          </>
        )}
      </Button>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

type PageResult =
  | { kind: "card"; data: PaymentIntentResponse; amount: number }
  | { kind: "cash"; data: PaymentResponse;        amount: number };

export function CreatePaymentIntentPage() {
  const navigate       = useNavigate();
  const [searchParams] = useSearchParams();

  const preselectedId = searchParams.get("appointmentId");

  const [chosen, setChosen] = useState<EnrichedAppointment | null>(null);
  const [pageResult, setPageResult] = useState<PageResult | null>(null);

  const { data: appointments = [] } = useGetAppointmentsQuery({}, { skip: !preselectedId });
  const { data: clients      = [] } = useGetClientsQuery(undefined, { skip: !preselectedId });

  // Derive the URL-preselected appointment instead of copying it into state
  const preselected = useMemo<EnrichedAppointment | null>(() => {
    if (!preselectedId) return null;
    const appt = appointments.find((a) => a.id === preselectedId);
    if (!appt) return null;
    const client = clients.find((c) => c.id === appt.clientId);
    const clientName = client ? `${client.firstName} ${client.lastName}` : "Client";
    return { ...appt, clientName };
  }, [preselectedId, appointments, clients]);

  const selected = chosen ?? preselected;

  const subtitle = pageResult
    ? pageResult.kind === "card"
      ? "Share the checkout link with your client."
      : "Cash payment awaiting collection."
    : selected
    ? "Confirm and create the payment."
    : "Pick an appointment with a pending deposit.";

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
          <div>
            <h1 className="text-lg font-semibold">New payment</h1>
            <p className="text-sm text-muted-foreground">{subtitle}</p>
          </div>
        </div>

        {pageResult ? (
          pageResult.kind === "card" ? (
            <CheckoutLinkPanel
              result={pageResult.data}
              appointmentId={selected?.id ?? ""}
              clientName={selected?.clientName ?? "Client"}
              appointmentDate={selected ? fmtDate(selected.date) : ""}
              amount={pageResult.amount}
            />
          ) : (
            <CashResultPanel
              result={pageResult.data}
              clientName={selected?.clientName ?? "Client"}
              appointmentDate={selected ? fmtDate(selected.date) : ""}
            />
          )
        ) : selected ? (
          <ConfirmPanel
            appointment={selected}
            onBack={() => setChosen(null)}
            onCardCreated={(res, amt) => setPageResult({ kind: "card", data: res, amount: amt })}
            onCashCreated={(res, amt) => setPageResult({ kind: "cash", data: res, amount: amt })}
          />
        ) : (
          <AppointmentPicker onSelect={setChosen} />
        )}
      </main>
    </div>
  );
}
