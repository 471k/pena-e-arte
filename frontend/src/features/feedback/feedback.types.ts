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

/** A report as its submitter sees it (GET feedback/mine, POST feedback). No adminNote: the API
 *  doesn't send the admin's internal note to the submitter at all. */
export interface MyFeedbackReportResponse {
  id:              string;
  type:            FeedbackType;
  title:           string;
  body:            string;
  status:          FeedbackStatus;
  studioName:      string;
  submitterRole:   string;
  createdAt:       string;
  resolvedAt:      string | null;
  attachmentUrls?: string[];
}

/** A report as the platform admin sees it (platform/feedback): adds the internal note. */
export interface FeedbackReportResponse extends MyFeedbackReportResponse {
  adminNote: string | null;
}

export interface SubmitFeedbackRequest {
  type:            FeedbackType;
  title:           string;
  body:            string;
  attachmentUrls?: string[];
}

export interface UpdateFeedbackStatusRequest {
  status:     FeedbackStatus;
  adminNote: string | null;
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
