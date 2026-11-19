import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  MrrDataPoint,
  MrrMovementsDataPoint,
  RevenueRetentionResponse,
  BackfillBilledAmountsResponse,
  BackfillRevenueLedgerResponse,
  PlatformStatsResponse,
  PlatformSubscriptionResponse,
  PlatformReferralCodeResponse,
  IndustryReportSummary,
  AdminStudioSummaryResponse,
  PlanUsageReportResponse,
  HelpSearchInsightsResponse,
  AuditLogPageResponse,
  AuditLogQueryParams,
  AuditLogVocabularyResponse,
  LiveTrafficSnapshotResponse,
  TrafficHistoryResponse,
  TrafficBreakdownResponse,
  ImpersonationTokenResponse,
  ImpersonationSessionPageResponse,
  AdminCancellationQuoteResponse,
  CancelSubscriptionArgs,
  FailedSubscriptionRefundsResponse,
} from "./platform.types";
import type { SubscriptionResponse } from "@/features/billing/billing.types";

export const platformApi = createApi({
  reducerPath: "platformApi",
  baseQuery,
  tagTypes: ["PlatformStats", "PlatformSubscription", "PlatformReferral", "IndustryReport", "MrrHistory", "AdminStudioSummary", "PlanUsageReport", "AuditLog", "LiveTraffic", "TrafficHistory", "TrafficBreakdown", "ImpersonationSession", "CancellationQuote", "FailedRefunds"],
  endpoints: (builder) => ({
    getPlatformStats: builder.query<PlatformStatsResponse, void>({
      query: () => "platform/stats",
      providesTags: ["PlatformStats"],
    }),
    getAdminStudioSummary: builder.query<AdminStudioSummaryResponse, string>({
      query: (studioId) => `platform/studios/${studioId}/summary`,
      providesTags: (_result, _err, studioId) => [{ type: "AdminStudioSummary", id: studioId }],
    }),
    getMrrHistory: builder.query<MrrDataPoint[], number | void>({
      query: (months) => months ? `platform/mrr-history?months=${months}` : "platform/mrr-history",
      providesTags: ["MrrHistory"],
    }),
    getMrrMovements: builder.query<MrrMovementsDataPoint[], number | void>({
      query: (months) => months ? `platform/mrr-movements?months=${months}` : "platform/mrr-movements",
      providesTags: ["MrrHistory"],
    }),
    getRevenueRetention: builder.query<RevenueRetentionResponse, void>({
      query: () => "platform/revenue-retention",
      providesTags: ["MrrHistory"],
    }),
    // One-time, idempotent data backfills (Batch 2b / Batch 3b). Both change what the dashboard's
    // revenue figures read, so they invalidate the same tags a subscription change would.
    backfillBilledAmounts: builder.mutation<BackfillBilledAmountsResponse, void>({
      query: () => ({ url: "platform/subscriptions/backfill-billed-amounts", method: "POST" }),
      invalidatesTags: ["PlatformStats", "MrrHistory", "PlatformSubscription"],
    }),
    backfillRevenueLedger: builder.mutation<BackfillRevenueLedgerResponse, void>({
      query: () => ({ url: "platform/subscriptions/backfill-revenue-ledger", method: "POST" }),
      invalidatesTags: ["MrrHistory"],
    }),
    // Every studio's subscription, or one studio's with { studioId } (the detail page, D08).
    getPlatformSubscriptions: builder.query<PlatformSubscriptionResponse[], { studioId?: string } | void>({
      query: (arg) => ({ url: "platform/subscriptions", params: arg?.studioId ? { studioId: arg.studioId } : undefined }),
      providesTags: ["PlatformSubscription"],
    }),
    extendTrial: builder.mutation<void, { studioId: string; additionalDays: number }>({
      query: ({ studioId, additionalDays }) => ({
        url: `platform/subscriptions/${studioId}/trial`,
        method: "PATCH",
        body: { additionalDays },
      }),
      invalidatesTags: ["PlatformSubscription", "PlatformStats"],
    }),
    getIndustryReports: builder.query<IndustryReportSummary[], void>({
      query: () => "platform/reports/industry",
      providesTags: ["IndustryReport"],
    }),
    triggerIndustryReport: builder.mutation<void, void>({
      query: () => ({
        url:    "platform/reports/industry/trigger",
        method: "POST",
      }),
      // No cache invalidation — the Hangfire job is async and takes minutes.
      // The page will show a "Queued" confirmation; the user refreshes manually.
    }),
    // Every referral code, or one studio's with { studioId } (the detail page, D08).
    getPlatformReferralCodes: builder.query<PlatformReferralCodeResponse[], { studioId?: string } | void>({
      query: (arg) => ({ url: "platform/referral-codes", params: arg?.studioId ? { studioId: arg.studioId } : undefined }),
      providesTags: ["PlatformReferral"],
    }),
    deactivateReferralCode: builder.mutation<void, string>({
      query: (id) => ({
        url: `platform/referral-codes/${id}/deactivate`,
        method: "PATCH",
      }),
      invalidatesTags: ["PlatformReferral"],
    }),
    generateReferralCodeForStudio: builder.mutation<
      PlatformReferralCodeResponse,
      { studioId: string; expiresAt?: string }
    >({
      query: ({ studioId, expiresAt }) => ({
        url:    `platform/studios/${studioId}/referral-codes`,
        method: "POST",
        body:   expiresAt ? { expiresAt } : undefined,
      }),
      invalidatesTags: ["PlatformReferral", "PlatformStats"],
    }),
    reactivateReferralCode: builder.mutation<void, string>({
      query: (id) => ({
        url:    `platform/referral-codes/${id}/reactivate`,
        method: "PATCH",
      }),
      invalidatesTags: ["PlatformReferral"],
    }),
    deleteReferralCode: builder.mutation<void, string>({
      query: (id) => ({
        url:    `platform/referral-codes/${id}`,
        method: "DELETE",
      }),
      invalidatesTags: ["PlatformReferral"],
    }),
    activateSubscriptionManually: builder.mutation<
      SubscriptionResponse,
      { studioId: string; planId: string; note?: string }
    >({
      query: ({ studioId, ...body }) => ({
        url:    `platform/studios/${studioId}/subscription/activate`,
        method: "POST",
        body,
      }),
      invalidatesTags: ["PlatformSubscription", "PlatformStats"],
    }),
    // D03: what cancelling will do, read fresh every time the review opens (never from cache: it is the figure the
    // admin confirms, and the server refuses a cancel whose figure has moved).
    getCancellationQuote: builder.query<AdminCancellationQuoteResponse, string>({
      query: (studioId) => `platform/studios/${studioId}/subscription/cancel-quote`,
      providesTags: (_result, _err, studioId) => [{ type: "CancellationQuote", id: studioId }],
      keepUnusedDataFor: 0,
    }),
    cancelSubscription: builder.mutation<void, CancelSubscriptionArgs>({
      query: ({ studioId, override: overrideRule, reason, confirmed }) => ({
        url:    `platform/subscriptions/${studioId}/cancel`,
        method: "PATCH",
        // The override and reason only when overriding (the formula is the default); the reviewed consequence always.
        body: {
          override: overrideRule ?? null,
          reason:   overrideRule ? reason : null,
          confirmedStatus:       confirmed.status,
          confirmedInvoiceId:    confirmed.invoiceId,
          confirmedRefundAmount: confirmed.refundAmount,
        },
      }),
      invalidatesTags: (_result, _err, { studioId }) => [
        "PlatformSubscription", "PlatformStats", "FailedRefunds",
        { type: "AdminStudioSummary", id: studioId }, { type: "CancellationQuote", id: studioId },
      ],
    }),
    // D07: failed yearly-cancellation refunds across all studios, newest first.
    getFailedRefunds: builder.query<FailedSubscriptionRefundsResponse, void>({
      query: () => "platform/refunds/failed",
      providesTags: ["FailedRefunds"],
    }),
    setDunningExclusion: builder.mutation<void, { studioId: string; excluded: boolean }>({
      query: ({ studioId, excluded }) => ({
        url:    `platform/studios/${studioId}/subscription/dunning-exclusion`,
        method: "POST",
        body:   { excluded },
      }),
      invalidatesTags: ["PlatformSubscription"],
    }),
    getPlanUsageReport: builder.query<PlanUsageReportResponse, void>({
      query: () => "platform/plan-usage-report",
      providesTags: ["PlanUsageReport"],
    }),
    getHelpSearchInsights: builder.query<HelpSearchInsightsResponse, { days?: number } | void>({
      query: (args) => `platform/help-search-insights${args?.days ? `?days=${args.days}` : ""}`,
      providesTags: ["PlatformStats"],
    }),
    getAuditLog: builder.query<AuditLogPageResponse, AuditLogQueryParams | void>({
      query: (params) => ({ url: "platform/audit-log", params: params ?? undefined }),
      providesTags: ["AuditLog"],
    }),
    // The named action and target-type choices for the filters; fixed for a given deploy.
    getAuditLogVocabulary: builder.query<AuditLogVocabularyResponse, void>({
      query: () => "platform/audit-log/vocabulary",
      keepUnusedDataFor: 3600,
    }),
    getLiveTrafficSnapshot: builder.query<LiveTrafficSnapshotResponse, void>({
      query: () => "platform/traffic/live",
      providesTags: ["LiveTraffic"],
    }),
    getTrafficHistory: builder.query<TrafficHistoryResponse, { days?: number } | void>({
      query: (args) => `platform/traffic/history${args?.days ? `?days=${args.days}` : ""}`,
      providesTags: ["TrafficHistory"],
    }),
    getTrafficBreakdown: builder.query<TrafficBreakdownResponse, { days?: number } | void>({
      query: (args) => `platform/traffic/breakdown${args?.days ? `?days=${args.days}` : ""}`,
      providesTags: ["TrafficBreakdown"],
    }),
    startImpersonation: builder.mutation<ImpersonationTokenResponse, { studioId: string; reasonCode: string }>({
      query: ({ studioId, reasonCode }) => ({
        url:    `platform/studios/${studioId}/impersonate`,
        method: "POST",
        body:   { reasonCode },
      }),
      invalidatesTags: ["ImpersonationSession"],
    }),
    endImpersonationSession: builder.mutation<void, string>({
      query: (sessionId) => ({
        url:    `platform/impersonation-sessions/${sessionId}/end`,
        method: "POST",
      }),
      invalidatesTags: ["ImpersonationSession"],
    }),
    getImpersonationSessions: builder.query<ImpersonationSessionPageResponse, { studioId?: string; page?: number; pageSize?: number } | void>({
      query: (params) => ({ url: "platform/impersonation-sessions", params: params ?? undefined }),
      providesTags: ["ImpersonationSession"],
    }),
  }),
});

export const {
  useGetPlatformStatsQuery,
  useGetAdminStudioSummaryQuery,
  useGetMrrHistoryQuery,
  useGetMrrMovementsQuery,
  useGetRevenueRetentionQuery,
  useBackfillBilledAmountsMutation,
  useBackfillRevenueLedgerMutation,
  useGetPlatformSubscriptionsQuery,
  useExtendTrialMutation,
  useGetIndustryReportsQuery,
  useTriggerIndustryReportMutation,
  useGetPlatformReferralCodesQuery,
  useDeactivateReferralCodeMutation,
  useGenerateReferralCodeForStudioMutation,
  useReactivateReferralCodeMutation,
  useDeleteReferralCodeMutation,
  useActivateSubscriptionManuallyMutation,
  useCancelSubscriptionMutation,
  useGetCancellationQuoteQuery,
  useGetFailedRefundsQuery,
  useSetDunningExclusionMutation,
  useGetPlanUsageReportQuery,
  useGetHelpSearchInsightsQuery,
  useGetAuditLogQuery,
  useGetAuditLogVocabularyQuery,
  useGetLiveTrafficSnapshotQuery,
  useGetTrafficHistoryQuery,
  useGetTrafficBreakdownQuery,
  useStartImpersonationMutation,
  useEndImpersonationSessionMutation,
  useGetImpersonationSessionsQuery,
} = platformApi;
