import { Wallet } from "lucide-react";
import { Link } from "react-router-dom";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useGetMyEarningsQuery } from "../reportsApi";
import { RevenueTrendChart } from "./RevenueTrendChart";

function formatCurrency(value: number): string {
  return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(value);
}

function formatDate(iso: string | null): string {
  if (!iso) return "—";
  return new Date(iso).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
}

export function MyEarningsPage() {
  useDocumentMeta({ title: "My Earnings — TattooOS", canonical: "/earnings" });

  const { data, isLoading, isError, error, refetch } = useGetMyEarningsQuery();

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
            <Card>
              <CardHeader className="pb-2">
                <CardTitle className="text-sm">Earnings trend (last 12 months)</CardTitle>
              </CardHeader>
              <CardContent className="pt-0">
                {data.monthlyTrend.every((p) => p.revenue === 0) ? (
                  <p className="h-[160px] flex items-center justify-center text-xs text-muted-foreground">
                    No earnings recorded yet.
                  </p>
                ) : (
                  <RevenueTrendChart data={data.monthlyTrend} />
                )}
              </CardContent>
            </Card>

            <Card>
              <CardHeader className="pb-2 flex flex-row items-center justify-between">
                <CardTitle className="text-sm">Payments (last 30 days)</CardTitle>
                <span className="text-sm font-semibold tabular-nums">{formatCurrency(data.periodTotal)}</span>
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
                          <span className="text-xs tabular-nums font-semibold">{formatCurrency(line.amount)}</span>
                        </div>
                        <div className="text-[11px] text-muted-foreground">
                          {formatDate(line.appointmentDate)}
                        </div>
                        {line.splits.length > 0 && (
                          <ul className="mt-1 space-y-0.5">
                            {line.splits.map((split) => (
                              <li
                                key={split.id}
                                className="flex items-center justify-between text-[11px] text-muted-foreground pl-2"
                              >
                                <span>{split.label}</span>
                                <span className="tabular-nums">{formatCurrency(split.amount)}</span>
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
          </>
        )}
      </main>
    </div>
  );
}
