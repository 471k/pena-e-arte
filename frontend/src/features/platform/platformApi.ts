import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  MrrDataPoint,
  PlatformStatsResponse,
  PlatformSubscriptionResponse,
  PlatformReferralCodeResponse,
  IndustryReportSummary,
  IssuerStudioSummaryResponse,
  PlanUsageReportResponse,
} from "./platform.types";
import type { SubscriptionResponse } from "@/features/billing/billing.types";

export const platformApi = createApi({
  reducerPath: "platformApi",
  baseQuery,
  tagTypes: ["PlatformStats", "PlatformSubscription", "PlatformReferral", "IndustryReport", "MrrHistory", "IssuerStudioSummary", "PlanUsageReport"],
  endpoints: (builder) => ({
    getPlatformStats: builder.query<PlatformStatsResponse, void>({
      query: () => "platform/stats",
      providesTags: ["PlatformStats"],
    }),
    getIssuerStudioSummary: builder.query<IssuerStudioSummaryResponse, string>({
      query: (studioId) => `platform/studios/${studioId}/summary`,
      providesTags: (_result, _err, studioId) => [{ type: "IssuerStudioSummary", id: studioId }],
    }),
    getMrrHistory: builder.query<MrrDataPoint[], number | void>({
      query: (months) => months ? `platform/mrr-history?months=${months}` : "platform/mrr-history",
      providesTags: ["MrrHistory"],
    }),
    getPlatformSubscriptions: builder.query<PlatformSubscriptionResponse[], void>({
      query: () => "platform/subscriptions",
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
    getPlatformReferralCodes: builder.query<PlatformReferralCodeResponse[], void>({
      query: () => "platform/referral-codes",
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
    cancelSubscription: builder.mutation<void, string>({
      query: (studioId) => ({
        url:    `platform/subscriptions/${studioId}/cancel`,
        method: "PATCH",
      }),
      invalidatesTags: ["PlatformSubscription", "PlatformStats"],
    }),
    getPlanUsageReport: builder.query<PlanUsageReportResponse, void>({
      query: () => "platform/plan-usage-report",
      providesTags: ["PlanUsageReport"],
    }),
  }),
});

export const {
  useGetPlatformStatsQuery,
  useGetIssuerStudioSummaryQuery,
  useGetMrrHistoryQuery,
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
  useGetPlanUsageReportQuery,
} = platformApi;
