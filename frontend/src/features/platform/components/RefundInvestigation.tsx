import { Link } from "react-router-dom";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useGetFailedRefundsQuery } from "@/features/platform/platformApi";
import type { RecentRefundResponse } from "@/features/platform/platform.types";
import { formatUtc, nextActionFor, refundAmount } from "./subscriptionConsequences";
import { describeLoadError } from "./describeLoadError";

const STATUS_PILL: Record<RecentRefundResponse["status"], string> = {
  Failed:    "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300",
  Succeeded: "bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-300",
  Pending:   "bg-amber-100 text-amber-800 dark:bg-amber-900/30 dark:text-amber-300",
};

/**
 * The studio page's recent refunds (A6 + D07). A failed one shows its reason, when it failed, the invoice and the next
 * action as text: the reason used to be a hover-only tooltip, out of reach by keyboard or touch.
 */
export function RecentRefundsList({ refunds }: { refunds: RecentRefundResponse[] }) {
  if (refunds.length === 0) return null;
  return (
    <div className="border-t pt-3 space-y-2">
      <p className="text-[11px] text-muted-foreground font-medium uppercase">Recent refunds</p>
      <ul className="space-y-2">
        {refunds.map((refund, i) => (
          <li key={`${refund.invoiceReference}-${i}`} className="text-xs space-y-1">
            <div className="flex items-center justify-between gap-2">
              <span className="tabular-nums">
                {refundAmount(refund.amount, refund.currency)} · {formatUtc(refund.createdAt)}
              </span>
              <span className={`px-1.5 py-0.5 rounded-full text-[10px] font-medium shrink-0 ${STATUS_PILL[refund.status]}`}>
                {refund.status}
              </span>
            </div>
            {refund.status === "Failed" && (
              <div className="rounded-md border border-destructive/30 px-2 py-1.5 space-y-0.5">
                <p>
                  <span className="font-medium">Failed {formatUtc(refund.updatedAt ?? refund.createdAt)}:</span>{" "}
                  {refund.failureReason ?? "Stripe gave no reason."}
                </p>
                <p className="text-muted-foreground">Invoice {refund.invoiceReference}</p>
                <p className="text-muted-foreground">{nextActionFor(refund.reachedStripe, refund.invoiceReference)}</p>
              </div>
            )}
          </li>
        ))}
      </ul>
    </div>
  );
}

/**
 * Failed refunds across every studio (D07): what failed, when, against which invoice, for which studio, why, and what
 * to do next. Before this, finding a failure meant already knowing which studio's page to open.
 */
export function FailedRefundsCard() {
  const { data, isLoading, isError, error, refetch, isFetching } = useGetFailedRefundsQuery();

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm font-medium">Failed refunds</CardTitle>
      </CardHeader>
      <CardContent className="space-y-2">
        {isLoading ? (
          <div role="status" aria-label="Loading failed refunds" className="space-y-2">
            <Skeleton className="h-10 w-full" />
            <Skeleton className="h-10 w-full" />
          </div>
        ) : isError || !data ? (
          // Never "no failed refunds" when the real answer is "couldn't check" (D02's rule, applied here).
          <div className="space-y-1">
            <p role="alert" className="text-xs text-destructive-text">
              {describeLoadError(error, "list of failed refunds").message}
            </p>
            {describeLoadError(error, "list of failed refunds").retryable && (
              <Button size="sm" variant="outline" className="h-7 px-2 text-xs" disabled={isFetching} onClick={() => void refetch()}>
                Try again
              </Button>
            )}
          </div>
        ) : data.items.length === 0 ? (
          <p className="text-xs text-muted-foreground">No failed refunds. Every refund sent so far went through or is still on its way.</p>
        ) : (
          <>
            {data.totalCount > data.items.length && (
              <p className="text-xs text-muted-foreground">
                Showing the newest {data.items.length} of {data.totalCount} failed refunds.
              </p>
            )}
            <ul className="divide-y">
              {data.items.map((refund) => (
                <li key={refund.id} className="py-2 first:pt-0 last:pb-0 text-xs space-y-0.5">
                  <div className="flex items-center justify-between gap-2">
                    <Link to={`/platform/studios/${refund.studioId}`} className="font-medium text-link hover:underline">
                      {refund.studioName}
                    </Link>
                    <span className="tabular-nums font-medium">{refundAmount(refund.amount, refund.currency)}</span>
                  </div>
                  <p>
                    Failed {formatUtc(refund.failedAt)}: {refund.failureReason ?? "Stripe gave no reason."}
                  </p>
                  <p className="text-muted-foreground">
                    Invoice {refund.invoiceReference} · issued {formatUtc(refund.createdAt)}
                  </p>
                  <p className="text-muted-foreground">{nextActionFor(refund.reachedStripe, refund.invoiceReference)}</p>
                </li>
              ))}
            </ul>
          </>
        )}
      </CardContent>
    </Card>
  );
}
