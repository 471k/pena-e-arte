import { useId, useState } from "react";
import { Link } from "react-router-dom";
import { CheckCircle2, ChevronDown, RotateCw } from "lucide-react";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Money } from "@/shared/components/Money";
import { cn } from "@/shared/utils/cn";
import { withStudioTimeZone } from "@/shared/utils/formatInStudioTimezone";
import { useSuspensionAwareError } from "@/shared/hooks/useSuspensionAwareError";
import { useGetOwnerExceptionsQuery } from "@/features/appointments/appointmentsApi";
import { useGetMyStudioQuery } from "@/features/studios/studiosApi";
import { describeDeposit } from "@/features/payments/depositOutcome";
import { CashDepositConfirmButton } from "@/features/payments/components/CashDepositConfirmButton";
import {
  OwnerExceptionKind,
  type OwnerExceptionGroup,
  type OwnerExceptionItem,
} from "../dashboard.types";

/**
 * "Needs you" (E05a, O01): the owner's home leads with what needs a decision, from GET
 * /dashboard/exceptions. Every count is the server's real count. Groups arrive in the server's order
 * (soonest appointment first, so anything overdue leads) and only when they have something in them;
 * nothing at all is the good state and says so.
 */

/** How many items each group shows before the rest go behind "Show more". */
export const VISIBLE_PER_GROUP = 3;

interface GroupCopy {
  title:    (windowDays: number) => string;
  /** Where the owner sees the whole list when there are more than the server sent. */
  moreHref: string;
  moreLabel: string;
}

const COPY: Record<OwnerExceptionKind, GroupCopy> = {
  [OwnerExceptionKind.Unassigned]: {
    title: () => "Requests with no artist",
    moreHref: "/schedule", moreLabel: "the schedule",
  },
  [OwnerExceptionKind.AwaitingConfirmation]: {
    title: () => "Waiting for confirmation",
    moreHref: "/schedule", moreLabel: "the schedule",
  },
  [OwnerExceptionKind.CashToConfirm]: {
    title: () => "Cash to confirm",
    moreHref: "/payments", moreLabel: "Payments",
  },
  [OwnerExceptionKind.UnpaidDeposit]: {
    title: (d) => `Deposits not paid (next ${d} days)`,
    moreHref: "/schedule", moreLabel: "the schedule",
  },
  [OwnerExceptionKind.Preparation]: {
    title: (d) => `Not ready yet (next ${d} days)`,
    moreHref: "/schedule", moreLabel: "the schedule",
  },
};

function formatWhen(iso: string, timeZone: string | undefined): string {
  return new Date(iso).toLocaleString("en-GB", withStudioTimeZone({
    weekday: "short", day: "numeric", month: "short", hour: "2-digit", minute: "2-digit",
  }, timeZone));
}

/** The line under the client's name that says what the decision is about. */
function Detail({ kind, item }: { kind: OwnerExceptionKind; item: OwnerExceptionItem }) {
  if (kind === OwnerExceptionKind.Preparation && item.readiness) {
    return (
      <span className="text-warning-text">
        Preparation: {item.readiness.satisfied} of {item.readiness.required} done
      </span>
    );
  }
  if (item.deposit) {
    const d = item.deposit;
    return (
      <span>
        Deposit <Money amount={d.amount} currency={d.currency} frozen={d.payment} />
        {" · "}
        {d.payment ? describeDeposit(d.payment).ownerDetail : "No payment started yet"}
      </span>
    );
  }
  return <span>{item.artistName ?? "No artist assigned"}</span>;
}

function Action({ kind, item }: { kind: OwnerExceptionKind; item: OwnerExceptionItem }) {
  if (kind === OwnerExceptionKind.CashToConfirm && item.deposit?.payment) {
    const p = item.deposit.payment;
    return (
      <CashDepositConfirmButton
        paymentId={p.id}
        clientName={item.clientName}
        amount={p.amount}
        currency={p.currency}
      />
    );
  }
  const label = kind === OwnerExceptionKind.Unassigned ? "Assign"
    : kind === OwnerExceptionKind.AwaitingConfirmation ? "Review"
    : "Open";
  return (
    <Link
      to={`/appointments/${item.appointmentId}`}
      className="text-xs text-link underline underline-offset-2 shrink-0"
      aria-label={`${label}: ${item.clientName}'s booking`}
    >
      {label}
    </Link>
  );
}

function ItemRow({ kind, item, timeZone }: { kind: OwnerExceptionKind; item: OwnerExceptionItem; timeZone: string | undefined }) {
  return (
    <li className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 py-2.5">
      <div className="min-w-0 space-y-0.5">
        <p className="text-sm break-words">
          <span className="font-medium tabular-nums">{formatWhen(item.appointmentDate, timeZone)}</span>
          {" · "}
          <span className="font-medium">{item.clientName}</span>
        </p>
        <p className="text-xs text-muted-foreground break-words">
          <Detail kind={kind} item={item} />
        </p>
      </div>
      <Action kind={kind} item={item} />
    </li>
  );
}

function Group({ group, windowDays, timeZone }: { group: OwnerExceptionGroup; windowDays: number; timeZone: string | undefined }) {
  const [open, setOpen] = useState(false);
  const headingId = useId();
  const regionId = useId();
  const copy = COPY[group.kind];
  const visible = group.items.slice(0, VISIBLE_PER_GROUP);
  const rest = group.items.slice(VISIBLE_PER_GROUP);
  const notSent = group.total - group.items.length;

  return (
    <section aria-labelledby={headingId} data-testid={`exception-group-${group.kind}`} className="px-4 py-3">
      <h3 id={headingId} className="text-sm font-medium">
        {copy.title(windowDays)} <span className="text-muted-foreground font-normal tabular-nums">({group.total})</span>
      </h3>
      <ul className="divide-y divide-border">
        {visible.map((item, i) => (
          <ItemRow key={`${item.appointmentId}-${i}`} kind={group.kind} item={item} timeZone={timeZone} />
        ))}
      </ul>
      {rest.length > 0 && (
        <>
          <button
            type="button"
            className="mt-1 flex items-center gap-1 text-xs text-link underline underline-offset-2"
            aria-expanded={open}
            aria-controls={regionId}
            onClick={() => setOpen((v) => !v)}
          >
            {open ? "Show fewer" : `Show ${rest.length} more`}
            <ChevronDown className={cn("h-3 w-3 transition-transform", open && "rotate-180")} aria-hidden="true" />
          </button>
          {/* `hidden` while collapsed, so nothing inside is focusable or announced (E04c's pattern). */}
          <ul id={regionId} hidden={!open} aria-label={`More: ${copy.title(windowDays)}`} className="divide-y divide-border border-t border-border">
            {rest.map((item, i) => (
              <ItemRow key={`${item.appointmentId}-more-${i}`} kind={group.kind} item={item} timeZone={timeZone} />
            ))}
          </ul>
        </>
      )}
      {notSent > 0 && (
        <p className="mt-1 text-xs text-muted-foreground">
          {notSent} more not listed here. See all of them in{" "}
          <Link to={copy.moreHref} className="text-link underline underline-offset-2">{copy.moreLabel}</Link>.
        </p>
      )}
    </section>
  );
}

export function OwnerExceptions() {
  // Refetch on every visit to the dashboard: some changes (a client paying, a new booking from the
  // public page) happen without an action in this browser to invalidate the cache.
  const { data, isLoading, isError, refetch, isFetching } =
    useGetOwnerExceptionsQuery(undefined, { refetchOnMountOrArgChange: true });
  const { data: studio } = useGetMyStudioQuery();
  const errorMessage = useSuspensionAwareError(isError, "We couldn't load what needs you.");

  return (
    <Card data-testid="owner-exceptions">
      <CardContent className="p-0">
        <div className="flex items-center justify-between px-4 pt-4 pb-1">
          <h2 className="text-sm font-medium">Needs you</h2>
        </div>

        {isLoading && (
          <div className="px-4 pb-4 space-y-2" aria-label="Loading what needs you" data-testid="exceptions-loading">
            <Skeleton className="h-4 w-1/2" />
            <Skeleton className="h-4 w-3/4" />
            <Skeleton className="h-4 w-2/3" />
          </div>
        )}

        {!isLoading && errorMessage && (
          <div className="px-4 pb-4 flex items-center justify-between gap-3">
            <p className="text-sm text-destructive-text" role="alert">{errorMessage}</p>
            <Button size="sm" variant="outline" className="h-7 text-xs gap-1" disabled={isFetching} onClick={() => void refetch()}>
              <RotateCw className="h-3.5 w-3.5" aria-hidden="true" />
              Try again
            </Button>
          </div>
        )}

        {!isLoading && !isError && data && data.groups.length === 0 && (
          <div className="px-4 pb-6 pt-2 flex flex-col items-center gap-2 text-center" data-testid="exceptions-empty">
            <CheckCircle2 className="h-8 w-8 text-success-text" aria-hidden="true" />
            <p className="text-sm font-medium">Nothing needs you right now</p>
            <p className="text-xs text-muted-foreground max-w-sm">
              Requests with no artist, bookings waiting for confirmation, cash to confirm, unpaid deposits
              and sessions that aren't ready will show up here.
            </p>
          </div>
        )}

        {!isLoading && !isError && data && data.groups.length > 0 && (
          <div className="divide-y divide-border">
            {data.groups.map((g) => (
              <Group key={g.kind} group={g} windowDays={data.windowDays} timeZone={studio?.timezone} />
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
