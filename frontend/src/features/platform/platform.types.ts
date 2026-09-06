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
  cancelAtPeriodEnd: boolean;
}

export interface PlatformReferralCodeResponse {
  id:               string;
  studioId:         string;
  studioName:       string;
  code:             string;
  shareUrl:         string;
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

export interface AdminStudioSummaryResponse {
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

export interface HelpQueryFrequency {
  query:      string;
  count:      number;
  rolesAsked: string[];
}

export interface HelpSearchInsightsResponse {
  totalSearches:     number;
  days:              number;
  topQueries:        HelpQueryFrequency[];
  zeroResultQueries: HelpQueryFrequency[];
}

export interface AuditLogEntryResponse {
  id:          string;
  actorUserId: string;
  actorRole:   string;
  action:      string;
  targetType:  string;
  targetId:    string;
  studioId:    string | null;
  metadata:    string;
  createdAt:   string;
}

export interface AuditLogPageResponse {
  items:      AuditLogEntryResponse[];
  totalCount: number;
  page:       number;
  pageSize:   number;
}

export interface AuditLogQueryParams {
  action?:     string;
  targetType?: string;
  from?:       string;
  to?:         string;
  page?:       number;
  pageSize?:   number;
}

export interface LiveVisitorResponse {
  visitorId:   string;
  role:        string | null;
  studioId:    string | null;
  studioName:  string | null;
  countryCode: string | null;
  city:        string | null;
  latitude:    number | null;
  longitude:   number | null;
  deviceType:  string | null;
  browser:     string | null;
  path:        string;
  connectedAt: string;
}

export interface LiveTrafficSnapshotResponse {
  totalActive: number;
  guestCount:  number;
  roleCounts:  Record<string, number>;
  visitors:    LiveVisitorResponse[];
}

export interface TrafficHistoryDataPoint {
  date:         string;
  guestCount:   number;
  clientCount:  number;
  artistCount:  number;
  ownerCount:   number;
  adminCount:  number;
}

export interface TrafficHistoryResponse {
  days:       number;
  dataPoints: TrafficHistoryDataPoint[];
}

export interface TrafficCountryCount {
  countryCode: string | null;
  country:     string | null;
  count:       number;
}

export interface TrafficNamedCount {
  name:  string;
  count: number;
}

export interface TrafficBreakdownResponse {
  days:             number;
  topCountries:     TrafficCountryCount[];
  deviceBreakdown:  TrafficNamedCount[];
  browserBreakdown: TrafficNamedCount[];
  topPages:         TrafficNamedCount[];
  topNetworks:      TrafficNamedCount[];
}
