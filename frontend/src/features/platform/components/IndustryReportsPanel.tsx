import { Download, FileText, Loader2 } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import { useGetIndustryReportsQuery } from "@/features/platform/platformApi";

export function IndustryReportsPanel() {
  const { data: reports, isLoading } = useGetIndustryReportsQuery();

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-8">
        <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
      </div>
    );
  }

  return (
    <div className="p-6 max-w-2xl mx-auto">
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <FileText className="h-5 w-5" />
            Industry Analytics Reports
          </CardTitle>
        </CardHeader>
        <CardContent>
          {!reports || reports.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              Reports are generated on the 1st of each month.
            </p>
          ) : (
            <ul className="space-y-2">
              {reports.map((report) => (
                <li
                  key={report.period}
                  className="flex items-center justify-between rounded-md border p-3"
                >
                  <span className="text-sm font-medium">{report.period}</span>
                  <Button variant="ghost" size="sm" asChild>
                    <a href={report.downloadUrl} download={`industry-report-${report.period}.json`}>
                      <Download className="h-4 w-4 mr-1" />
                      Download JSON
                    </a>
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
