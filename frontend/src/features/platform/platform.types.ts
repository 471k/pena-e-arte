export interface PlatformStatsResponse {
  totalStudios:        number;
  activeSubscriptions: number;
  trialStudios:        number;
  gracePeriodStudios:  number;
  pastDueStudios:      number;
  cancelledStudios:    number;
  suspendedStudios:    number;
  mrr:                 number;
  mrrGrowthPercent:    number;
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
  trialExpiresAt:  string | null;
  currentPeriodEnd: string;
  isSuspended:     boolean;
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

export interface MrrDataPoint {
  month: string;
  mrr:   number;
}

export interface IssuerStudioSummaryResponse {
  ownerEmail:       string;
  ownerDisplayName: string;
  artistCount:      number;
  clientCount:      number;
  appointmentCount: number;
}

export interface StudioPlanUsageRow {
  studioId:                string;
  studioName:               string;
  planName:                 string;
  artistCount:              number;
  maxArtists:               number | null;
  appointmentsThisMonth:    number;
  maxAppointmentsPerMonth:  number | null;
  notificationsThisMonth:   number;
  maxNotificationsPerMonth: number | null;
  storageGbUsed:            number;
  maxStorageGb:             number | null;
}

export interface PlanUsageReportResponse {
  studios: StudioPlanUsageRow[];
}
