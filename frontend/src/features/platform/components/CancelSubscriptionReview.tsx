import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { extractErrorMessage } from "@/shared/utils/extractErrorMessage";
import { formatCurrency } from "@/shared/utils/formatCurrency";
import { useCancelSubscriptionMutation, useGetCancellationQuoteQuery } from "@/features/platform/platformApi";
import type { AdminCancellationQuoteResponse, CancelRefundChoice } from "@/features/platform/platform.types";
import { ConsequenceReview, ReviewAlert } from "./ConsequenceReview";
import { refundFor } from "./subscriptionConsequences";

function isQuoteChanged(error: unknown): boolean {
  if (typeof error !== "object" || error === null || !("data" in error)) return false;
  const data = (error as { data: unknown }).data;
  return typeof data === "object" && data !== null && (data as { code?: unknown }).code === "CANCELLATION_QUOTE_CHANGED";
}

function money(amount: number, currency: string | null): string {
  return currency ? formatCurrency(amount, currency.toUpperCase()) : amount.toFixed(2);
}

function refundLine(quote: AdminCancellationQuoteResponse, choice: CancelRefundChoice): string {
  if (!quote.refundApplies) return "No refund: only a yearly plan paid through Stripe has one.";
  const paid = money(quote.amountPaid ?? 0, quote.currency);
  if (choice === "AdminNone") return `No refund, by your override, of the ${paid} paid.`;
  if (choice === "AdminFull") return `A full refund of ${money(quote.fullRefund, quote.currency)}, by your override, to the card that paid.`;
  const months = quote.monthsUsed ?? 0;
  return `A refund of ${money(quote.formulaRefund, quote.currency)} of the ${paid} paid (${months} month${months === 1 ? "" : "s"} used), to the card that paid.`;
}

interface CancelSubscriptionReviewProps {
  studioId:   string;
  studioName: string;
  /** Element ids stay unique per row: e.g. "detail" or the studio id. */
  idSuffix:   string;
  onDone:     () => void;
  onBack:     () => void;
}

/**
 * Cancel a studio's subscription (D03). The consequence is read from the server every time this opens, and the
 * cancel sends back what was shown: if anything moved in between (a month boundary, a status change, a different
 * rule), the server refuses with 409, nothing is cancelled, and this shows the new consequence with the inputs kept.
 */
export function CancelSubscriptionReview({ studioId, studioName, idSuffix, onDone, onBack }: CancelSubscriptionReviewProps) {
  const quoteQuery = useGetCancellationQuoteQuery(studioId, { refetchOnMountOrArgChange: true });
  const [choice,  setChoice]  = useState<CancelRefundChoice>("formula");
  const [reason,  setReason]  = useState("");
  const [failure, setFailure] = useState<{ message: string; attempt: number; changed: boolean } | null>(null);
  const [cancelSub, { isLoading }] = useCancelSubscriptionMutation();

  const quote     = quoteQuery.isFetching ? undefined : quoteQuery.data;
  const overriding = choice !== "formula";
  const id         = `cancel-${idSuffix}`;

  async function submit() {
    if (!quote) return;
    const overrideRule = choice === "formula" ? undefined : choice;
    try {
      await cancelSub({
        studioId,
        override:  overrideRule,
        reason:    overrideRule ? reason.trim() : undefined,
        confirmed: { status: quote.status, invoiceId: quote.invoiceId, refundAmount: refundFor(quote, choice) },
      }).unwrap();
      toast.success(`Subscription cancelled for ${studioName}`);
      onDone();
    } catch (err) {
      const changed = isQuoteChanged(err);
      setFailure((prev) => ({
        message: extractErrorMessage(err, "The subscription wasn't cancelled."),
        attempt: (prev?.attempt ?? 0) + 1,
        changed,
      }));
      // Whatever went wrong, the consequence on screen may no longer be true: read it again before any retry.
      void quoteQuery.refetch();
    }
  }

  const happens = quoteQuery.isFetching
    ? [<span key="checking" role="status">Working out what cancelling will do…</span>]
    : quote
      ? [
          `The ${quote.planName ?? "subscription"} (${quote.billingInterval}, now ${quote.status}) is cancelled immediately, not at the end of the period.`,
          "The studio loses access straight away: its owner and artists can't use TattooOS until it subscribes again. Its data is kept.",
          ...(quote.billedThroughStripe ? ["Billing stops: its Stripe subscription is cancelled too."] : []),
          refundLine(quote, choice),
        ]
      : ["What cancelling will do couldn't be worked out, so it can't be confirmed yet."];

  const permanent = [
    "The cancellation can't be reversed. To come back, the studio subscribes again.",
    ...(quote && refundFor(quote, choice) > 0 ? ["A refund can't be recalled once it's sent to Stripe."] : []),
  ];

  const alert = quoteQuery.isError && !quoteQuery.isFetching ? (
    <ReviewAlert key="quote-error">
      <p className="font-medium">{extractErrorMessage(quoteQuery.error, "What cancelling will do couldn't be loaded.")}</p>
      <Button size="sm" variant="outline" className="h-7 px-2 text-xs" onClick={() => void quoteQuery.refetch()}>Try again</Button>
    </ReviewAlert>
  ) : failure ? (
    <ReviewAlert key={failure.attempt}>
      <p className="font-medium">{failure.changed ? failure.message : `Cancellation failed: ${failure.message}`}</p>
      <p>
        {quoteQuery.isFetching
          ? "Checking what cancelling will do now…"
          : failure.changed
            ? "What happens above is the new consequence. Your choices are kept."
            : "Nothing above has changed. Your choices are kept, so you can try again."}
      </p>
    </ReviewAlert>
  ) : null;

  return (
    <ConsequenceReview
      id={id}
      title="Cancel subscription"
      studioName={studioName}
      happens={happens}
      permanent={permanent}
      alert={alert}
      confirmLabel={failure ? "Try again" : "Cancel subscription"}
      destructive
      busy={isLoading}
      confirmDisabled={!quote || (overriding && reason.trim().length === 0)}
      onConfirm={() => void submit()}
      onBack={onBack}
    >
      {/* The rule only matters where there is something to refund; elsewhere the server ignores it, so it isn't offered. */}
      {quote?.refundApplies && (
        <fieldset className="space-y-1">
          <legend className="text-xs font-medium text-muted-foreground">Refund</legend>
          {([
            ["formula",   `Use the formula: ${money(quote.formulaRefund, quote.currency)}`],
            ["AdminFull", `Full refund: ${money(quote.fullRefund, quote.currency)}`],
            ["AdminNone", "No refund"],
          ] as const).map(([value, label]) => (
            <label key={value} className="flex items-center gap-1.5 text-xs">
              <input
                type="radio"
                name={`${id}-refund`}
                value={value}
                checked={choice === value}
                onChange={() => setChoice(value)}
                className="h-3 w-3"
              />
              {label}
            </label>
          ))}
        </fieldset>
      )}
      {quote?.refundApplies && overriding && (
        <div className="space-y-1">
          <Label htmlFor={`${id}-reason`} className="text-xs">
            Reason <span className="text-destructive-text">(required)</span>
          </Label>
          <Input
            id={`${id}-reason`}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder="e.g. Goodwill gesture per support ticket #123"
            className="h-8 text-xs"
          />
        </div>
      )}
    </ConsequenceReview>
  );
}
