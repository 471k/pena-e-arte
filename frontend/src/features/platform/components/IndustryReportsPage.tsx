import { BarChart3, ExternalLink, Loader2 } from "lucide-react";
import { Card, CardContent } from "@/shared/components/ui/card";
import { useGetIndustryReportsQuery } from "@/features/platform/platformApi";
import type { IndustryReportSummary } from "@/features/platform/platform.types";

function formatPeriod(period: string): string {
  const parts = period.split("-");
  if (parts.length === 2) {
    const [year, month] = parts;
    const date = new Date(parseInt(year), parseInt(month) - 1);
    return date.toLocaleDateString("en-GB", { month: "long", year: "numeric" });
  }
  return period;
}

interface ReportRowProps {
  report: IndustryReportSummary;
}

function ReportRow({ report }: ReportRowProps) {
  return (
    <Card>
      <CardContent className="p-4 flex items-center justify-between gap-4">
        <div className="space-y-0.5 min-w-0">
          <span className="font-medium text-sm">{formatPeriod(report.period)}</span>
          <p className="text-xs text-muted-foreground">
            Generated {new Date(report.generatedAt).toLocaleDateString("en-GB")}
          </p>
        </div>
        <a
          href={report.downloadUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="flex items-center gap-1.5 text-xs text-primary hover:underline shrink-0"
        >
          Open
          <ExternalLink className="h-3.5 w-3.5" />
        </a>
      </CardContent>
    </Card>
  );
}

export function IndustryReportsPage() {
  const { data: reports, isLoading, isError } = useGetIndustryReportsQuery();

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <BarChart3 className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Industry Reports</span>
      </header>

      <main className="max-w-xl mx-auto px-4 py-6 space-y-3">
        {isLoading && (
          <div className="flex items-center justify-center py-16 gap-2 text-muted-foreground">
            <Loader2 className="h-5 w-5 animate-spin" />
            <span className="text-sm">Loading…</span>
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">Failed to load reports.</p>
        )}

        {!isLoading && !isError && reports?.length === 0 && (
          <p className="text-center text-sm text-muted-foreground py-16">No reports published yet.</p>
        )}

        {!isLoading && !isError && reports?.map((report) => (
          <ReportRow key={report.period} report={report} />
        ))}
      </main>
    </div>
  );
}
