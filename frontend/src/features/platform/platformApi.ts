import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  MrrDataPoint,
  PlatformStatsResponse,
  PlatformSubscriptionResponse,
  PlatformReferralCodeResponse,
  IndustryReportSummary,
} from "./platform.types";
import type { SubscriptionResponse } from "@/features/billing/billing.types";

export const platformApi = createApi({
  reducerPath: "platformApi",
  baseQuery,
  tagTypes: ["PlatformStats", "PlatformSubscription", "PlatformReferral", "IndustryReport", "MrrHistory"],
  endpoints: (builder) => ({
    getPlatformStats: builder.query<PlatformStatsResponse, void>({
      query: () => "platform/stats",
      providesTags: ["PlatformStats"],
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
  }),
});

export const {
  useGetPlatformStatsQuery,
  useGetMrrHistoryQuery,
  useGetPlatformSubscriptionsQuery,
  useExtendTrialMutation,
  useGetIndustryReportsQuery,
  useGetPlatformReferralCodesQuery,
  useDeactivateReferralCodeMutation,
  useActivateSubscriptionManuallyMutation,
  useCancelSubscriptionMutation,
} = platformApi;
