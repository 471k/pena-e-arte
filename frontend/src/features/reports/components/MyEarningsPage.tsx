import { Wallet } from "lucide-react";
import { Link } from "react-router-dom";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useGetMyEarningsQuery } from "../reportsApi";
import { useGetMyStudioQuery } from "@/features/studios/studiosApi";
import { withStudioTimeZone } from "@/shared/utils/formatInStudioTimezone";
import { formatCurrency } from "@/shared/utils/formatCurrency";
import { RevenueTrendChart } from "./RevenueTrendChart";
import { MyBoothRentSection } from "@/features/booth-rent/components/MyBoothRentSection";
import { describeArtistArrangement } from "@/features/studios/payModel";
import type { ArtistEarningsResponse, EarningsPaymentLine } from "../report.types";

function formatDate(iso: string | null, studioTimezone: string | undefined): string {
  if (!iso) return "—";
  return new Date(iso).toLocaleDateString(
    "en-GB", withStudioTimeZone({ day: "numeric", month: "short", year: "numeric" }, studioTimezone));
}

/**
 * A07a: the headline is the artist's share under their arrangement (their own, else the studio's), with
 * what clients paid beside it. Before A07a this page showed the whole client payment as "earnings".
 * When the studio hasn't set what the arrangement needs, there is no total at all: not 0, and not the
 * client total with a caveat.
 */
function ShareUnknown() {
  return (
    <Card data-testid="earnings-share-unknown">
      <CardContent className="py-8 flex flex-col items-center gap-2 text-center">
        <Wallet className="h-8 w-8 text-muted-foreground/40" aria-hidden="true" />
        <p className="text-sm font-medium">We can't work out your share yet</p>
        <p className="text-xs text-muted-foreground max-w-sm">
          Your studio hasn't set how artists get paid yet, so we can't work out your share. Ask your studio
          owner to set it in Studio settings.
        </p>
      </CardContent>
    </Card>
  );
}

function ShareHeadline({ data }: { data: ArtistEarningsResponse }) {
  const label = describeArtistArrangement(data.arrangement, data.commissionRate, data.flatFeePerSession, data.currency);
  return (
    <Card data-testid="earnings-share">
      <CardContent className="p-4 grid gap-3 sm:grid-cols-2">
        <div className="space-y-0.5">
          <p className="text-xs font-medium text-muted-foreground">Your share (last 30 days) · {label}</p>
          <p className="text-3xl font-bold tabular-nums" data-testid="earnings-period-total">
            {formatCurrency(data.periodTotal ?? 0, data.currency)}
          </p>
        </div>
        <div className="space-y-0.5 sm:text-right">
          <p className="text-xs text-muted-foreground">What clients paid (last 30 days)</p>
          <p className="text-lg tabular-nums text-muted-foreground" data-testid="earnings-client-paid-total">
            {formatCurrency(data.clientPaidTotal, data.currency)}
          </p>
        </div>
      </CardContent>
    </Card>
  );
}

/**
 * What the client paid, and what happened to it. A fully refunded payment used to read "Client paid ALL 0":
 * the client did pay, it was refunded afterwards (E05b-1, carried finding 1). The original figure stays,
 * struck through, with the refund named; a partial refund shows both amounts.
 */
function ClientPaid({ line, currency }: { line: EarningsPaymentLine; currency: string }) {
  const refunded = line.refundedAmount ?? 0;
  const isRefunded = line.status === "Refunded" && refunded > 0;
  if (!isRefunded) {
    return <span className="tabular-nums">Client paid {formatCurrency(line.amount, currency)}</span>;
  }
  const original = formatCurrency(line.amount, currency);
  if (line.clientPaidAmount <= 0) {
    return (
      <span className="tabular-nums" data-testid="earnings-line-refunded">
        Client paid <s aria-label={`${original}, refunded`}>{original}</s>{" "}
        <span className="font-medium text-foreground">Refunded</span>
      </span>
    );
  }
  return (
    <span className="tabular-nums" data-testid="earnings-line-refunded">
      Client paid {original} · {formatCurrency(refunded, currency)} refunded
    </span>
  );
}

export function MyEarningsPage() {
  useDocumentMeta({ title: "My Earnings — TattooOS", canonical: "/earnings" });

  const { data, isLoading, isError, error, refetch } = useGetMyEarningsQuery();
  const { data: studio } = useGetMyStudioQuery();

  // A 404 here means the caller (an owner who hasn't enabled their own artist profile, or
  // no longer has one) has no Artist row to attribute earnings to — not a transient failure,
  // so retrying can never succeed. Same pattern as ConsentFormDetailPage.
  const isNoArtistProfile = isError && !!error && "status" in error && error.status === 404;

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Wallet className="h-5 w-5" aria-hidden="true" />
        <span className="font-semibold tracking-tight">My Earnings</span>
      </header>

      <main className="max-w-3xl mx-auto px-4 py-6 space-y-4">
        {isLoading && (
          <div className="space-y-4">
            <Skeleton className="h-[220px] w-full rounded-lg" />
            <Skeleton className="h-40 w-full rounded-lg" />
          </div>
        )}

        {isNoArtistProfile && (
          <div className="flex flex-col items-center justify-center gap-3 py-16 text-center">
            <Wallet className="h-8 w-8 text-muted-foreground/40" aria-hidden="true" />
            <div className="space-y-1">
              <p className="text-sm font-medium">No artist profile yet</p>
              <p className="text-xs text-muted-foreground max-w-xs">
                Earnings are tracked against an artist profile. Enable your own artist profile to start seeing them here.
              </p>
            </div>
            <Link to="/artists" className="text-xs underline text-muted-foreground hover:text-foreground">
              Enable your artist profile
            </Link>
          </div>
        )}

        {isError && !isNoArtistProfile && (
          <div className="flex flex-col items-center justify-center gap-3 py-16 text-center">
            <p className="text-sm text-destructive-text">Failed to load your earnings.</p>
            <button
              type="button"
              onClick={() => refetch()}
              className="text-xs underline text-muted-foreground hover:text-foreground"
            >
              Try again
            </button>
          </div>
        )}

        {!isLoading && !isError && data && (
          <>
            {data.excludedOtherCurrencyCount > 0 && (
              <p className="text-xs text-muted-foreground">
                {data.excludedOtherCurrencyCount} payment{data.excludedOtherCurrencyCount !== 1 ? "s" : ""} in
                another currency aren't included in this total.
              </p>
            )}

            {data.shareIsKnown ? <ShareHeadline data={data} /> : <ShareUnknown />}

            {data.shareIsKnown && (
              <Card>
                <CardHeader className="pb-2">
                  <CardTitle className="text-sm">Your share (last 12 months)</CardTitle>
                </CardHeader>
                <CardContent className="pt-0">
                  {data.monthlyTrend.every((p) => (p.artistAmount ?? 0) === 0) ? (
                    <p className="h-[160px] flex items-center justify-center text-xs text-muted-foreground">
                      No earnings recorded yet.
                    </p>
                  ) : (
                    <RevenueTrendChart
                      data={data.monthlyTrend.map((p) => ({ month: p.month, revenue: p.artistAmount ?? 0 }))}
                      currency={data.currency}
                    />
                  )}
                </CardContent>
              </Card>
            )}

            <Card>
              <CardHeader className="pb-2">
                <CardTitle className="text-sm">Payments received (last 30 days)</CardTitle>
              </CardHeader>
              <CardContent className="pt-0">
                {data.payments.length === 0 ? (
                  <p className="py-8 text-center text-xs text-muted-foreground">
                    No payments recorded yet.
                  </p>
                ) : (
                  <ul className="divide-y rounded-md border">
                    {data.payments.map((line) => (
                      <li key={line.paymentId} className="px-3 py-2.5 space-y-1">
                        <div className="flex items-center justify-between gap-2">
                          <span className="text-xs font-medium truncate">{line.clientName}</span>
                          {line.shareIsKnown && line.artistAmount !== null && (
                            <span className="text-xs tabular-nums font-semibold" data-testid="earnings-line-share">
                              {formatCurrency(line.artistAmount, data.currency)}
                            </span>
                          )}
                        </div>
                        {/* Both dates, labelled: the list is filtered on when the payment came in, which is the
                            date an artist checks against their bank or cash; a deposit can come in weeks
                            before the session (A07a cleanup, finding 5). */}
                        <div className="flex flex-wrap items-center justify-between gap-x-2 gap-y-0.5 text-[11px] text-muted-foreground">
                          <span data-testid="earnings-line-dates">
                            Paid <time dateTime={line.paidAt}>{formatDate(line.paidAt, studio?.timezone)}</time>
                            {" · "}
                            Session {line.appointmentDate
                              ? <time dateTime={line.appointmentDate}>{formatDate(line.appointmentDate, studio?.timezone)}</time>
                              : "—"}
                          </span>
                          <ClientPaid line={line} currency={data.currency} />
                        </div>
                        {line.splits.length > 0 && (
                          <ul className="mt-1 space-y-0.5">
                            {line.splits.map((split) => (
                              <li
                                key={split.id}
                                className="flex items-center justify-between text-[11px] text-muted-foreground pl-2"
                              >
                                <span>{split.label}</span>
                                <span className="tabular-nums">{formatCurrency(split.amount, data.currency)}</span>
                              </li>
                            ))}
                          </ul>
                        )}
                      </li>
                    ))}
                  </ul>
                )}
              </CardContent>
            </Card>

            <MyBoothRentSection />
          </>
        )}
      </main>
    </div>
  );
}
