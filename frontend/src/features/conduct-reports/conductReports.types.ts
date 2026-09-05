export const REPORT_CATEGORY = {
  Scam:                   "Scam",
  SexualMisconduct:       "SexualMisconduct",
  UnsafeHygienePractices: "UnsafeHygienePractices",
  Harassment:             "Harassment",
  Discrimination:         "Discrimination",
  PoorServiceQuality:     "PoorServiceQuality",
  Other:                  "Other",
} as const;
export type ReportCategory = (typeof REPORT_CATEGORY)[keyof typeof REPORT_CATEGORY];

export const REPORT_CATEGORY_LABEL: Record<ReportCategory, string> = {
  Scam:                   "Scam or fraud",
  SexualMisconduct:       "Sexual misconduct or abuse",
  UnsafeHygienePractices: "Unsafe or unsanitary practices",
  Harassment:             "Harassment",
  Discrimination:         "Discrimination",
  PoorServiceQuality:     "Poor service quality",
  Other:                  "Other",
};

export const REPORT_STATUS = {
  Open:      "Open",
  Reviewing: "Reviewing",
  Resolved:  "Resolved",
  Dismissed: "Dismissed",
} as const;
export type ReportStatus = (typeof REPORT_STATUS)[keyof typeof REPORT_STATUS];

// Mirrors ReportCategoryClassifier.cs — keep in sync if the backend taxonomy changes.
export const HIGH_SEVERITY_CATEGORIES: ReadonlySet<ReportCategory> = new Set([
  "Scam", "SexualMisconduct", "UnsafeHygienePractices", "Harassment", "Discrimination",
]);

export interface ConductReportResponse {
  id:              string;
  studioId:        string;
  studioName:      string;
  artistId:        string | null;
  artistName:      string | null;
  appointmentId:   string;
  appointmentDate: string;
  category:        ReportCategory;
  isHighSeverity:  boolean;
  reason:          string;
  attachmentUrls:  string[];
  status:          ReportStatus;
  resolutionNote:  string | null;
  resolvedAt:      string | null;
  createdAt:       string;
  reporterUserId:  string | null;
  reporterName:    string | null;
}

export interface ReportableAppointment {
  id:              string;
  date:            string;
  durationMinutes: number;
  status:          string;
}

export interface FileConductReportArgs {
  appointmentId:  string;
  category:       ReportCategory;
  reason:         string;
  attachmentUrls?: string[];
}
