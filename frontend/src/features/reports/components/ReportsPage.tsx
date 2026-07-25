import { BarChart3 } from "lucide-react";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useGetRevenueSummaryQuery } from "../reportsApi";
import { RevenueTrendChart } from "./RevenueTrendChart";

function formatCurrency(value: number): string {
  return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(value);
}

export function ReportsPage() {
  useDocumentMeta({ title: "Reports — TattooOS", canonical: "/reports" });

  const { data, isLoading, isError, refetch } = useGetRevenueSummaryQuery();

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <BarChart3 className="h-5 w-5" aria-hidden="true" />
        <span className="font-semibold tracking-tight">Reports</span>
      </header>

      <main className="max-w-3xl mx-auto px-4 py-6 space-y-4">
        {isLoading && (
          <div className="space-y-4">
            <Skeleton className="h-[220px] w-full rounded-lg" />
            <Skeleton className="h-40 w-full rounded-lg" />
          </div>
        )}

        {isError && (
          <div className="flex flex-col items-center justify-center gap-3 py-16 text-center">
            <p className="text-sm text-destructive">Failed to load revenue report.</p>
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
                <CardTitle className="text-sm">Revenue trend (last 12 months)</CardTitle>
              </CardHeader>
              <CardContent className="pt-0">
                {data.monthlyTrend.every((p) => p.revenue === 0) ? (
                  <p className="h-[160px] flex items-center justify-center text-xs text-muted-foreground">
                    No revenue recorded yet.
                  </p>
                ) : (
                  <RevenueTrendChart data={data.monthlyTrend} />
                )}
              </CardContent>
            </Card>

            <Card>
              <CardHeader className="pb-2">
                <CardTitle className="text-sm">Revenue by artist (last 30 days)</CardTitle>
              </CardHeader>
              <CardContent className="pt-0">
                {data.perArtist.length === 0 ? (
                  <p className="py-8 text-center text-xs text-muted-foreground">
                    No revenue recorded yet.
                  </p>
                ) : (
                  <div className="overflow-x-auto rounded-md border">
                    <table className="w-full text-xs">
                      <thead>
                        <tr className="border-b bg-muted/40 text-left text-muted-foreground">
                          <th className="px-3 py-2 font-medium">Artist</th>
                          <th className="px-3 py-2 font-medium text-right">Revenue</th>
                        </tr>
                      </thead>
                      <tbody>
                        {data.perArtist.map((row) => (
                          <tr key={row.artistId} className="border-b last:border-b-0">
                            <td className="px-3 py-2 font-medium truncate max-w-[220px]">{row.artistName}</td>
                            <td className="px-3 py-2 text-right tabular-nums">{formatCurrency(row.revenue)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </CardContent>
            </Card>
          </>
        )}
      </main>
    </div>
  );
}
