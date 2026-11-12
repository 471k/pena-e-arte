import { useId, useState } from "react";
import { Link } from "react-router-dom";
import { ChevronDown, Hourglass, RotateCw } from "lucide-react";
import { Card } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Money } from "@/shared/components/Money";
import { cn } from "@/shared/utils/cn";
import { useGetUnsettledPaymentsQuery } from "../paymentsApi";
import {
  UnsettledPaymentKind,
  type ProviderStatusAtCheck,
  type UnsettledPaymentGroup,
  type UnsettledPaymentItem,
} from "../payment.types";

/**
 * "Not settled yet" (E05b-2, O06): the card payments the daily reconciliation check hasn't settled, on the
 * owner's Payments page. Everything shown is what that check last recorded; this screen works nothing out
 * for itself. No totals: these rows are, by definition, money that hasn't settled, and a sum of them would
 * read as money the studio has. Cash promises are not repeated here: the dashboard's Needs you list owns
 * that count, and the section links to it.
 */

const COPY: Record<UnsettledPaymentKind, { title: string; explain: string }> = {
  [UnsettledPaymentKind.ProviderDisagrees]: {
    title:   "Card provider disagrees",
    explain: "The card provider's last answer doesn't match our record, and the check left it for you to look at. Open the payment to see what to do.",
  },
  [UnsettledPaymentKind.OnHold]: {
    title:   "On hold, not charged yet",
    explain: "The client's card was authorised but not charged. Charge or release each hold from its payment page; card holds don't last forever.",
  },
  [UnsettledPaymentKind.AwaitingCardPayment]: {
    title:   "Waiting for the client to pay",
    explain: "A card payment was started but not paid. Each one is cancelled automatically after the time shown, and the client can start a new one.",
  },
};

const PROVIDER_SAID: Record<ProviderStatusAtCheck, string> = {
  Pending:    "not paid",
  Authorized: "held",
  Captured:   "charged",
  Canceled:   "cancelled",
  Refunded:   "refunded",
  Failed:     "failed",
};

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString("en-GB", {
    day: "numeric", month: "short", hour: "2-digit", minute: "2-digit",
  });
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
}

function AmountLine({ item, kind }: { item: UnsettledPaymentItem; kind: UnsettledPaymentKind }) {
  // Each label says exactly what the number is: a hold isn't money received, an unpaid order isn't owed.
  const suffix =
    kind === UnsettledPaymentKind.OnHold ? "on hold, not charged" :
    kind === UnsettledPaymentKind.AwaitingCardPayment ? "asked, not paid" :
    item.status === "Captured" ? "on hold, by our record" : "not paid, by our record";
  return (
    <span className="text-sm">
      <Money amount={item.amount} currency={item.currency} className="font-semibold" />{" "}
      <span className="text-muted-foreground">{suffix}</span>
    </span>
  );
}

function CheckLine({ item, generatedAt }: { item: UnsettledPaymentItem; generatedAt: string }) {
  const said = item.providerCheckedAt
    ? item.providerStatusAtCheck
      ? `The card provider said ${PROVIDER_SAID[item.providerStatusAtCheck]}`
      : "The card provider had no record of it"
    : null;
  return (
    <p className="text-xs text-muted-foreground" data-testid="unsettled-check">
      {item.providerCheckedAt
        ? <>Last checked {formatDateTime(item.providerCheckedAt)}. {said}.</>
        : <>Not checked with the card provider yet.</>}
      {item.checkOverdue && (
        <span className="ml-1 font-medium text-warning-text" data-testid="unsettled-check-overdue">
          Check overdue: the daily check hasn&apos;t reached it.
        </span>
      )}
      {item.releaseDeadline && (
        <span className="block">
          {new Date(item.releaseDeadline) <= new Date(generatedAt)
            ? "Expired: it will be cancelled at the next check."
            : `Cancelled automatically after ${formatDateTime(item.releaseDeadline)}.`}
        </span>
      )}
    </p>
  );
}

function ItemRow({ item, kind, generatedAt }: { item: UnsettledPaymentItem; kind: UnsettledPaymentKind; generatedAt: string }) {
  return (
    <li className="px-4 py-2.5">
      <Link
        to={`/payments/${item.appointmentId}`}
        className="block space-y-0.5 rounded-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        data-testid="unsettled-item"
      >
        <span className="flex flex-wrap items-baseline justify-between gap-x-3">
          <span className="text-sm font-medium">{item.clientName || "—"}</span>
          <span className="text-xs text-muted-foreground">
            Session {formatDate(item.appointmentDate)}
            {item.appointmentStatus === "Cancelled" ? " (cancelled)" : ""}
          </span>
        </span>
        <AmountLine item={item} kind={kind} />
        <CheckLine item={item} generatedAt={generatedAt} />
      </Link>
    </li>
  );
}

function Group({ group, generatedAt }: { group: UnsettledPaymentGroup; generatedAt: string }) {
  // A disagreement needs a person, so it starts open; the routine groups start closed.
  const [open, setOpen] = useState(group.kind === UnsettledPaymentKind.ProviderDisagrees);
  const regionId = useId();
  const copy = COPY[group.kind];
  const more = group.total - group.items.length;
  return (
    <section className="border-t border-border" data-testid={`unsettled-group-${group.kind}`}>
      <button
        type="button"
        className="flex w-full items-center justify-between gap-3 px-4 py-3 text-left"
        aria-expanded={open}
        aria-controls={regionId}
        onClick={() => setOpen((o) => !o)}
      >
        <span className="text-sm font-medium">
          {copy.title} <span className="text-muted-foreground">({group.total})</span>
        </span>
        <ChevronDown className={cn("h-4 w-4 shrink-0 transition-transform", open && "rotate-180")} aria-hidden="true" />
      </button>
      {/* `hidden` while collapsed, so it stays in the DOM (find-in-page, tests) but isn't focusable or announced. */}
      <div id={regionId} hidden={!open}>
        <p className="px-4 pb-2 text-xs text-muted-foreground">{copy.explain}</p>
        <ul className="divide-y divide-border">
          {group.items.map((item) => (
            <ItemRow key={item.paymentId} item={item} kind={group.kind} generatedAt={generatedAt} />
          ))}
        </ul>
        {more > 0 && (
          <p className="px-4 py-2 text-xs text-muted-foreground" data-testid="unsettled-more">
            {group.items.length} oldest shown of {group.total}. Use the Pending or On hold filter below for the rest.
          </p>
        )}
      </div>
    </section>
  );
}

export function UnsettledPaymentsSection() {
  const { currentData: response, isFetching, isError, refetch } = useGetUnsettledPaymentsQuery();
  // A body without groups is treated as not loaded rather than read as "nothing waiting".
  const currentData = response && Array.isArray(response.groups) ? response : undefined;

  return (
    <Card data-testid="unsettled-payments" className="overflow-hidden">
      <div className="px-4 pt-3 pb-2">
        <h2 className="flex items-center gap-2 text-sm font-semibold">
          <Hourglass className="h-4 w-4" aria-hidden="true" />
          Not settled yet
        </h2>
        <p className="text-xs text-muted-foreground">
          Card payments the daily check with the card provider hasn&apos;t settled. None of this is money received yet.
          Cash a client said they&apos;d bring is on your{" "}
          <Link to="/dashboard" className="text-link underline-offset-2 hover:underline">dashboard under Needs you</Link>.
        </p>
      </div>

      {currentData === undefined && !isError && (
        <div className="space-y-2 px-4 pb-4" aria-label="Loading unsettled payments" data-testid="unsettled-loading">
          <Skeleton className="h-4 w-2/3" />
          <Skeleton className="h-4 w-1/2" />
        </div>
      )}

      {isError && (
        <p className="px-4 pb-4 text-sm text-destructive-text" role="alert" data-testid="unsettled-error">
          Couldn&apos;t load the payments that haven&apos;t settled.{" "}
          <button type="button" className="inline-flex items-center gap-1 underline" onClick={() => refetch()} disabled={isFetching}>
            <RotateCw className="h-3 w-3" aria-hidden="true" />
            Try again
          </button>
        </p>
      )}

      {currentData && currentData.groups.length === 0 && (
        <p className="px-4 pb-4 text-sm text-muted-foreground" data-testid="unsettled-empty">
          No card payments are waiting to settle.
        </p>
      )}

      {currentData && currentData.groups.map((g) => (
        <Group key={g.kind} group={g} generatedAt={currentData.generatedAt} />
      ))}
    </Card>
  );
}
