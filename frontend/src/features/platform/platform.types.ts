export interface PlatformStatsResponse {
  totalStudios:        number;
  activeSubscriptions: number;
  trialStudios:        number;
  gracePeriodStudios:  number;
  mrr:                 number;
  trialConversionRate: number;
  newStudiosThisMonth: number;
}

export type SubscriptionStatus =
  | "Trialing"
  | "Active"
  | "PastDue"
  | "Cancelled"
  | "GracePeriod"
  | "NoSubscription";

export interface PlatformSubscriptionResponse {
  studioId:        string;
  studioName:      string;
  studioSlug:      string;
  subscriptionId:  string | null;
  status:          SubscriptionStatus;
  planName:        string | null;
  trialExpiresAt:  string;
  currentPeriodEnd: string;
}

export interface PlatformReferralCodeResponse {
  id:               string;
  studioId:         string;
  studioName:       string;
  code:             string;
  isActive:         boolean;
  isSingleUse:      boolean;
  createdAt:        string;
  expiresAt:        string | null;
  redemptionCount:  number;
}

export interface IndustryReportSummary {
  period:      string;
  generatedAt: string;
  downloadUrl: string;
}
