export interface PlatformStatsResponse {
  totalStudios:        number;
  activeSubscriptions: number;
  trialStudios:        number;
  gracePeriodStudios:  number;
  pastDueStudios:      number;
  cancelledStudios:    number;
  suspendedStudios:    number;
  mrr:                 number;
  mrrGrowthPercent:    number | null;
  trialConversionRate: number;
  newStudiosThisMonth: number;
  payingStudios:       number;
  atRiskMrr:           number;
  scheduledChurnMrr:   number;
  pausedMrr:           number;
  discountsThisMonth:  number;
  refundsThisMonth:    number;
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
  pastDueSince?:   string | null;
  dunningExcludedManually?: boolean;
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
  month:       string;
  mrr:         number;
  /** True when the month predates the revenue ledger and was reconstructed from current state. */
  isEstimated: boolean;
}

export interface MrrMovementsDataPoint {
  month:        string;
  new:          number;
  expansion:    number;
  reactivation: number;
  /** Negative. */
  contraction:  number;
  /** Negative. */
  churn:        number;
  net:          number;
}

export interface CashBilledSnapshot {
  studioId:   string;
  /** Empty when the studio row no longer exists — the UI falls back to a short id. */
  studioName: string;
  studioSlug: string;
  price:      number;
}

export interface BackfillBilledAmountsResponse {
  cardBilledUpdated:   number;
  cardBilledSkipped:   number;
  /** Cash-billed subscriptions snapshotted from their plan's Monthly price — listed for review. */
  cashBilledSnapshots: CashBilledSnapshot[];
}

export interface BackfillRevenueLedgerResponse {
  created:               number;
  skippedAlreadyInLedger: number;
  skippedNotBilling:     number;
}

export interface RevenueRetentionResponse {
  /** Fractions (0.95 = 95%) for the last COMPLETED month; null when there was no MRR at its start. */
  grossRevenueRetention: number | null;
  netRevenueRetention:   number | null;
  startMrr:              number;
  /** First instant (UTC, ISO 8601) of the month the rates cover. */
  periodStart:           string;
}

export interface RecentRefundResponse {
  amount:           number;
  /** The refund's own currency (D07): never assumed. */
  currency:         string;
  status:           "Pending" | "Succeeded" | "Failed";
  createdAt:        string;
  /** When the row last changed: for a Failed refund, when it failed. */
  updatedAt:        string | null;
  failureReason:    string | null;
  /** The Stripe invoice the refund was issued against. */
  invoiceReference: string;
  /** Whether Stripe accepted the refund request. A Failed one that didn't may or may not have been made. */
  reachedStripe:    boolean;
}

/** D07: a failed yearly-cancellation refund on the platform-wide list. */
export interface FailedSubscriptionRefundResponse {
  id:               string;
  studioId:         string;
  studioName:       string;
  amount:           number;
  currency:         string;
  rule:             "YearlyFormula" | "AdminFull" | "AdminNone";
  createdAt:        string;
  failedAt:         string;
  failureReason:    string | null;
  invoiceReference: string;
  reachedStripe:    boolean;
}

export interface FailedSubscriptionRefundsResponse {
  items:      FailedSubscriptionRefundResponse[];
  /** All failed refunds; items holds at most the newest 50. */
  totalCount: number;
}

/**
 * D03: what an admin cancellation will do. Admin cancellation is immediate. refundApplies is true only for a Yearly,
 * Stripe-billed subscription with a paid invoice; otherwise every rule refunds nothing.
 */
export interface AdminCancellationQuoteResponse {
  status:              string;
  planName:            string | null;
  billingInterval:     "Monthly" | "Yearly";
  billedThroughStripe: boolean;
  refundApplies:       boolean;
  currency:            string | null;
  formulaRefund:       number;
  fullRefund:          number;
  monthsUsed:          number | null;
  amountPaid:          number | null;
  invoiceId:           string | null;
}

export type CancelRefundChoice = "formula" | "AdminFull" | "AdminNone";

export interface CancelSubscriptionArgs {
  studioId:  string;
  override?: "AdminFull" | "AdminNone";
  reason?:   string;
  /** The consequence the admin reviewed; the server refuses (409) when it no longer holds. */
  confirmed: { status: string; invoiceId: string | null; refundAmount: number };
}

export interface AdminStudioSummaryResponse {
  ownerEmail:       string;
  ownerDisplayName: string;
  artistCount:      number;
  clientCount:      number;
  appointmentCount: number;
  recentRefunds:    RecentRefundResponse[];
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
  /** Resolved by the admin read; null for a platform-wide entry. */
  studioName?: string | null;
  /** The actor's account name now (never a stored copy), for staff roles only; null when there's none on file. */
  actorName?: string | null;
  /** True when the actor's account no longer exists. */
  actorAccountDeleted?: boolean;
}

export interface AuditLogVocabularyResponse {
  actions:     string[];
  targetTypes: string[];
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
  studioId?:    string;
  actorUserId?: string;
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

export interface ImpersonationTokenResponse {
  sessionId:   string;
  accessToken: string;
  expiresAt:   string;
  studioId:    string;
  studioName:  string;
}

export interface ImpersonationSessionResponse {
  id:          string;
  actorUserId: string;
  studioId:    string;
  reasonCode:  string;
  createdAt:   string;
  expiresAt:   string;
  endedAt:     string | null;
}

export interface ImpersonationSessionPageResponse {
  items:      ImpersonationSessionResponse[];
  totalCount: number;
  page:       number;
  pageSize:   number;
}
