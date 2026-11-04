import type { ReportStatus } from "../conductReports.types";

// Shared between ConductReportsPage.tsx (owner/artist) and ConductReportInboxPage.tsx (admin)
// — was copy-pasted verbatim across both before this module existed. Split out from
// conductReportShared.tsx because that file's component exports tripped
// react-refresh/only-export-components when mixed with these non-component exports.

export const STATUS_BADGE: Record<ReportStatus, string> = {
  Open:      "bg-blue-500/15 text-info-text",
  Reviewing: "bg-amber-500/10 text-warning-text",
  Resolved:  "bg-green-500/10 text-success-text",
  Dismissed: "bg-muted text-muted-foreground",
};

export function fmt(date: string): string {
  return new Date(date).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
}
