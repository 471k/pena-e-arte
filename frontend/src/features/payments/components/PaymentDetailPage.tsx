import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import {
  ArrowLeft,
  Banknote,
  CheckCircle2,
  CreditCard,
  Download,
  Loader2,
  RotateCcw,
} from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { MoneyInput, hasAtMostCurrencyMinorUnits, tooManyDecimalsMessage } from "@/shared/components/ui/money-input";
import { Label } from "@/shared/components/ui/label";
import { cn } from "@/shared/utils/cn";
import { formatCurrency, currencyLabel } from "@/shared/utils/formatCurrency";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import {
  useGetPaymentByAppointmentQuery,
  useCaptureDepositMutation,
  useRefundPaymentMutation,
  useConfirmCashDepositMutation,
  useDownloadInvoiceMutation,
} from "../paymentsApi";
import { PaymentMethod, PaymentStatus } from "../payment.types";
import { describeDeposit, OUTCOME_BADGE_CLASSES, type DepositOutcomeKind } from "../depositOutcome";
import { SessionSplitsEditor } from "./SessionSplitsEditor";
import { Money } from "@/shared/components/Money";
import { formatAtFrozenRate, type FrozenReference } from "@/shared/hooks/useEurAllRate";

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit",
  });
}

function StatusBadge({ kind, label }: { kind: DepositOutcomeKind; label: string }) {
  return (
    <span className={cn("text-xs px-2 py-0.5 rounded-full font-medium", OUTCOME_BADGE_CLASSES[kind] ?? "bg-muted")}>
      {label}
    </span>
  );
}

// C-14: a card payment can be refunded several times, until the whole amount is back (POK allows
// several partial refunds). `remaining` = amount − already refunded; leaving the field blank refunds it.
function RefundSection({ paymentId, currency, remaining, alreadyRefunded, frozen }: {
  paymentId: string; currency: string; remaining: number; alreadyRefunded: number;
  /** The payment's frozen lek rate: refunds show lek at the original rate (spec D5). */
  frozen: FrozenReference;
}) {
  const money = (n: number) => formatAtFrozenRate(n, currency, frozen);
  const [open, setOpen]           = useState(false);
  const [amountStr, setAmountStr] = useState("");
  const [amountError, setAmountError] = useState<string | null>(null);
  const [refund, { isLoading }]   = useRefundPaymentMutation();
  const partlyRefunded = alreadyRefunded > 0;

  async function handleRefund() {
    const amount = amountStr ? parseFloat(amountStr) : undefined;
    setAmountError(null);
    if (amount !== undefined && !hasAtMostCurrencyMinorUnits(amount, currency)) {
      setAmountError(tooManyDecimalsMessage(currency));
      return;
    }
    if (amount !== undefined && amount > remaining) {
      setAmountError(`Only ${money(remaining)} is left to refund.`);
      return;
    }
    try {
      await refund({ id: paymentId, amount }).unwrap();
      setOpen(false);
      setAmountStr("");
      // G-4: the refund used to succeed silently (only the badge changed).
      toast.success(
        amount !== undefined || partlyRefunded
          ? `Refund of ${money(amount ?? remaining)} issued — it goes back to the client's card.`
          : "Full refund issued — it goes back to the client's card.",
      );
    } catch (e) {
      const err = e as { data?: { message?: string } } | undefined;
      toast.error(err?.data?.message ?? "Refund failed. Please try again.");
    }
  }

  if (!open) {
    return (
      <Button
        variant="outline"
        size="sm"
        onClick={() => setOpen(true)}
        className="gap-1.5 text-destructive-text hover:text-destructive-text"
      >
        <RotateCcw className="h-3.5 w-3.5" />
        {partlyRefunded ? `Refund more — ${money(remaining)} left` : "Refund"}
      </Button>
    );
  }

  return (
    <Card>
      <CardContent className="p-4 space-y-3">
        <p className="text-sm font-medium">Issue refund</p>
        {partlyRefunded && (
          <p className="text-xs text-muted-foreground">
            {money(alreadyRefunded)} already refunded · {money(remaining)} left to refund
          </p>
        )}
        <div className="space-y-1">
          <Label htmlFor="refund-amount" className="text-xs">
            Amount ({currencyLabel(currency)}) — leave blank to refund {partlyRefunded ? "the rest" : "in full"}
          </Label>
          <MoneyInput
            id="refund-amount"
            currency={currency}
            placeholder={partlyRefunded ? formatCurrency(remaining, currency) : "Full amount"}
            value={amountStr}
            onChange={(e) => { setAmountStr(e.target.value); setAmountError(null); }}
          />
          {amountError && <p className="text-xs text-destructive-text">{amountError}</p>}
        </div>
        <div className="flex gap-2">
          <Button
            variant="destructive"
            size="sm"
            onClick={handleRefund}
            disabled={isLoading}
            className="flex-1"
          >
            {isLoading ? (
              <>
                <Loader2 className="h-4 w-4 animate-spin" />
                Refunding…
              </>
            ) : (
              "Confirm refund"
            )}
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() => { setOpen(false); setAmountStr(""); }}
            disabled={isLoading}
            className="flex-1"
          >
            Cancel
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

export function PaymentDetailPage() {
  useDocumentMeta({ title: "Payment — TattooOS", canonical: "/payments" });

  const { appointmentId } = useParams<{ appointmentId: string }>();
  const navigate          = useNavigate();
  const canOwner          = usePermission(Role.Owner);

  const { data: payment, isLoading, isError } =
    useGetPaymentByAppointmentQuery(appointmentId!);

  const [capture,     { isLoading: isCapturing }]  = useCaptureDepositMutation();
  const [confirmCash, { isLoading: isConfirming }] = useConfirmCashDepositMutation();
  const [downloadInvoice, { isLoading: isDownloading }] = useDownloadInvoiceMutation();

  async function handleCapture(paymentId: string) {
    try {
      await capture(paymentId).unwrap();
      toast.success("Deposit captured.");
    } catch (e) {
      const err = e as { data?: { message?: string } } | undefined;
      toast.error(err?.data?.message ?? "Capture failed. Please try again.");
    }
  }

  async function handleDownloadInvoice(paymentId: string) {
    try {
      const blob = await downloadInvoice(paymentId).unwrap();
      const url  = URL.createObjectURL(blob);
      const a    = document.createElement("a");
      a.href     = url;
      a.download = `invoice-${paymentId.slice(0, 8)}.pdf`;
      a.click();
      URL.revokeObjectURL(url);
    } catch {
      toast.error("Failed to download invoice. Please try again.");
    }
  }

  async function handleConfirmCash(paymentId: string) {
    try {
      await confirmCash(paymentId).unwrap();
      toast.success("Cash deposit confirmed.");
    } catch (e) {
      const err = e as { data?: { message?: string } } | undefined;
      toast.error(err?.data?.message ?? "Confirmation failed. Please try again.");
    }
  }

  if (isLoading) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center gap-2 text-muted-foreground">
        <Loader2 className="h-5 w-5 animate-spin" />
        <span className="text-sm">Loading payment…</span>
      </div>
    );
  }

  if (isError || !payment) {
    // The backend returns a plain 404 for "no payment created yet on this
    // appointment" — that's an expected state here, not a fetch failure, so
    // the recovery action is "create one" rather than "retry".
    return (
      <div className="min-h-screen bg-background flex flex-col items-center justify-center gap-4">
        <p className="text-sm text-muted-foreground">No payment found for this appointment.</p>
        <div className="flex gap-2">
          <Button variant="ghost" size="sm" onClick={() => navigate("/payments")}>
            <ArrowLeft className="h-4 w-4 mr-1" />
            Payments
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() => navigate(`/payments/new?appointmentId=${appointmentId}`)}
          >
            <CreditCard className="h-4 w-4 mr-1" />
            Create payment
          </Button>
        </div>
      </div>
    );
  }

  const isAwaitingAction = payment.status === PaymentStatus.Pending
    || payment.status === PaymentStatus.CashPending;
  const isPaid       = payment.status === PaymentStatus.Paid;
  const isRefunded   = payment.status === PaymentStatus.Refunded;
  const isCard       = payment.method === PaymentMethod.Card;
  const isCash       = payment.method === PaymentMethod.Cash;
  const outcome      = describeDeposit(payment);
  // Only when money was actually charged/collected — a released hold has nothing to receipt (G-6).
  const hasReceipt   = (isPaid || isRefunded) && outcome.charged;
  // C-14: refundable while money is left. A Refunded payment with no refundedAmount predates
  // that field and was a full refund.
  const alreadyRefunded = payment.refundedAmount ?? (isRefunded ? payment.amount : 0);
  const leftToRefund    = Math.max(0, payment.amount - alreadyRefunded);
  const canRefund       = (isPaid || isRefunded) && leftToRefund > 0;

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

        <div className="flex items-center gap-2">
          {payment.status === PaymentStatus.Pending && canOwner && isCard && (
            <span className="text-xs text-muted-foreground">
              Awaiting client card authorization — share the payment link to collect the deposit.
            </span>
          )}

          {payment.status === PaymentStatus.Captured && canOwner && isCard && (
            <Button
              size="sm"
              onClick={() => handleCapture(payment.id)}
              disabled={isCapturing}
              className="gap-1.5"
            >
              {isCapturing ? (
                <>
                  <Loader2 className="h-3.5 w-3.5 animate-spin" />
                  Capturing…
                </>
              ) : (
                <>
                  <CheckCircle2 className="h-3.5 w-3.5" />
                  Capture deposit
                </>
              )}
            </Button>
          )}

          {/* C-15: a failed card payment (incl. a hold POK ended) can be asked for again, by card link
              or as cash — the API reuses this row for the new payment. */}
          {payment.status === PaymentStatus.Failed && canOwner && (
            <Button
              size="sm"
              variant="outline"
              onClick={() => navigate(`/payments/new?appointmentId=${appointmentId}`)}
              className="gap-1.5"
            >
              <CreditCard className="h-3.5 w-3.5" />
              New payment
            </Button>
          )}

          {payment.status === PaymentStatus.CashPending && canOwner && isCash && (
            <Button
              size="sm"
              onClick={() => handleConfirmCash(payment.id)}
              disabled={isConfirming}
              className="gap-1.5"
            >
              {isConfirming ? (
                <>
                  <Loader2 className="h-3.5 w-3.5 animate-spin" />
                  Confirming…
                </>
              ) : (
                <>
                  <Banknote className="h-3.5 w-3.5" />
                  Confirm cash received
                </>
              )}
            </Button>
          )}

          {canRefund && canOwner && isCard && (
            <RefundSection
              paymentId={payment.id}
              currency={payment.currency}
              remaining={leftToRefund}
              alreadyRefunded={alreadyRefunded}
              frozen={payment}
            />
          )}

          {hasReceipt && (
            <Button
              variant="outline"
              size="sm"
              onClick={() => handleDownloadInvoice(payment.id)}
              disabled={isDownloading}
              className="gap-1.5"
            >
              {isDownloading ? (
                <>
                  <Loader2 className="h-3.5 w-3.5 animate-spin" />
                  Downloading…
                </>
              ) : (
                <>
                  <Download className="h-3.5 w-3.5" />
                  Download receipt
                </>
              )}
            </Button>
          )}
        </div>
      </header>

      <main className="max-w-lg mx-auto px-4 py-8 space-y-6">
        <div className="flex items-center gap-4">
          <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-full bg-primary/10">
            {isCash
              ? <Banknote   className="h-6 w-6 text-primary" />
              : <CreditCard className="h-6 w-6 text-primary" />}
          </div>
          <div className="space-y-1">
            <div className="flex items-center gap-2">
              <h1 className="text-lg font-semibold"><Money amount={payment.amount} currency={payment.currency} frozen={payment} /></h1>
              <StatusBadge kind={outcome.kind} label={outcome.ownerLabel} />
            </div>
            {outcome.ownerDetail && (
              <p className="text-sm text-muted-foreground">{outcome.ownerDetail}</p>
            )}
            <p className="text-xs text-muted-foreground font-mono">
              {payment.id}
            </p>
          </div>
        </div>

        <Card>
          <CardContent className="p-4 space-y-2.5">
            <Row label="Method"      value={isCash ? "Cash" : "Card"} />
            <Row label="Appointment" value={payment.appointmentId} mono />
            {payment.paidAt && (
              <Row label="Paid at" value={formatDate(payment.paidAt)} />
            )}
            {payment.cashNote && (
              <Row label="Note" value={payment.cashNote} />
            )}
            {/* C-10: card payments go through POK; this is the POK order id (was labelled "Stripe PI"). */}
            {payment.providerReferenceId && (
              <Row label="POK order" value={payment.providerReferenceId} mono />
            )}
          </CardContent>
        </Card>

        {isAwaitingAction && isCash && (
          <div className="rounded-lg border border-orange-200 bg-orange-50/30 p-4 text-sm text-orange-800">
            Awaiting cash collection from client.
          </div>
        )}

        {canOwner && (
          <SessionSplitsEditor
            paymentId={payment.id}
            paymentAmount={payment.amount}
            currency={payment.currency}
            currentSplits={payment.splits ?? []}
            frozen={payment}
          />
        )}
      </main>
    </div>
  );
}

function Row({
  label,
  value,
  mono = false,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div className="flex items-start justify-between gap-3 text-sm">
      <span className="text-muted-foreground shrink-0">{label}</span>
      <span className={cn("text-right break-all", mono && "font-mono text-xs")}>
        {value}
      </span>
    </div>
  );
}
