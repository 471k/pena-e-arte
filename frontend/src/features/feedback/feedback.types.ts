export const FEEDBACK_TYPE = {
  BugReport:      "BugReport",
  FeatureRequest: "FeatureRequest",
  General:        "General",
  SupportRequest: "SupportRequest",
} as const;
export type FeedbackType = (typeof FEEDBACK_TYPE)[keyof typeof FEEDBACK_TYPE];

export const FEEDBACK_STATUS = {
  Open:      "Open",
  Reviewing: "Reviewing",
  Resolved:  "Resolved",
  Dismissed: "Dismissed",
} as const;
export type FeedbackStatus = (typeof FEEDBACK_STATUS)[keyof typeof FEEDBACK_STATUS];

export interface FeedbackReportResponse {
  id:              string;
  type:            FeedbackType;
  title:           string;
  body:            string;
  status:          FeedbackStatus;
  studioName:      string;
  submitterRole:   string;
  issuerNote:      string | null;
  createdAt:       string;
  resolvedAt:      string | null;
  attachmentUrls?: string[];
}

export interface SubmitFeedbackRequest {
  type:            FeedbackType;
  title:           string;
  body:            string;
  attachmentUrls?: string[];
}

export interface UpdateFeedbackStatusRequest {
  status:     FeedbackStatus;
  issuerNote: string | null;
}

export interface FeedbackMessageResponse {
  id:               string;
  feedbackReportId: string;
  authorUserId:     string;
  authorRole:       string;
  body:             string;
  createdAt:        string;
}

export interface PostFeedbackMessageRequest {
  body: string;
}
