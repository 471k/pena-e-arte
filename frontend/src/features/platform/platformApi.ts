import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  PlatformStatsResponse,
  PlatformSubscriptionResponse,
  PlatformReferralCodeResponse,
  IndustryReportSummary,
} from "./platform.types";

export const platformApi = createApi({
  reducerPath: "platformApi",
  baseQuery,
  tagTypes: ["PlatformStats", "PlatformSubscription", "PlatformReferral", "IndustryReport"],
  endpoints: (builder) => ({
    getPlatformStats: builder.query<PlatformStatsResponse, void>({
      query: () => "platform/stats",
      providesTags: ["PlatformStats"],
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
  }),
});

export const {
  useGetPlatformStatsQuery,
  useGetPlatformSubscriptionsQuery,
  useExtendTrialMutation,
  useGetIndustryReportsQuery,
  useGetPlatformReferralCodesQuery,
  useDeactivateReferralCodeMutation,
} = platformApi;
