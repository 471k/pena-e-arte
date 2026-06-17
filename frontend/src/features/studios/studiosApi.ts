import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";

export interface RegisterStudioRequest {
  name:          string;
  slug:          string;
  city:          string;
  latitude:      number;
  longitude:     number;
  ownerEmail:    string;
  referralCode?: string;
}

export interface StudioResponse {
  id:                   string;
  name:                 string;
  slug:                 string;
  city:                 string;
  latitude:             number;
  longitude:            number;
  showPlatformBranding: boolean;
  allowBrandingRemoval: boolean;
  trialExpiresAt:       string;
  createdAt:            string;
  isActive:             boolean;
}

export interface StudioMapItem {
  id: string;
  name: string;
  slug: string;
  latitude: number;
  longitude: number;
  city: string;
}

export interface ReferralCodeResponse {
  id:          string;
  code:        string;
  shareUrl:    string;
  isActive:    boolean;
  isSingleUse: boolean;
  createdAt:   string;
  expiresAt:   string | null;
}

export interface ReferralStatsResponse {
  code:             string | null;
  redemptionCount:  number;
  discountsApplied: number;
}

export interface UpdateStudioRequest {
  name:      string;
  city:      string;
  latitude:  number;
  longitude: number;
}

export const studiosApi = createApi({
  reducerPath: "studiosApi",
  baseQuery,
  tagTypes: ["Studio", "Referral"],
  endpoints: (builder) => ({
    registerStudio: builder.mutation<StudioResponse, RegisterStudioRequest>({
      query: (body) => ({ url: "studios", method: "POST", body }),
    }),
    getStudioMap: builder.query<StudioMapItem[], void>({
      query: () => "studios/map",
    }),
    getMyStudio: builder.query<StudioResponse, void>({
      query: () => "studios/me",
      providesTags: ["Studio"],
    }),
    updateMyStudio: builder.mutation<StudioResponse, UpdateStudioRequest>({
      query: (body) => ({ url: "studios/me", method: "PUT", body }),
      invalidatesTags: ["Studio"],
    }),
    // Issuer: list all studios
    getStudios: builder.query<StudioResponse[], void>({
      query: () => "studios",
      providesTags: ["Studio"],
    }),
    updateStudioBranding: builder.mutation<StudioResponse, { id: string; showPlatformBranding: boolean }>({
      query: ({ id, showPlatformBranding }) => ({
        url:    `studios/${id}/branding`,
        method: "PATCH",
        body:   { showPlatformBranding },
      }),
      invalidatesTags: ["Studio"],
    }),
    suspendStudio: builder.mutation<void, string>({
      query: (id) => ({ url: `studios/${id}/suspend`, method: "PATCH" }),
      invalidatesTags: ["Studio"],
      async onQueryStarted(id, { dispatch, queryFulfilled }) {
        const patch = dispatch(
          studiosApi.util.updateQueryData("getStudios", undefined, (draft) => {
            const s = draft.find((x) => x.id === id);
            if (s) s.isActive = false;
          }),
        );
        try { await queryFulfilled; } catch { patch.undo(); }
      },
    }),
    unsuspendStudio: builder.mutation<void, string>({
      query: (id) => ({ url: `studios/${id}/unsuspend`, method: "PATCH" }),
      invalidatesTags: ["Studio"],
      async onQueryStarted(id, { dispatch, queryFulfilled }) {
        const patch = dispatch(
          studiosApi.util.updateQueryData("getStudios", undefined, (draft) => {
            const s = draft.find((x) => x.id === id);
            if (s) s.isActive = true;
          }),
        );
        try { await queryFulfilled; } catch { patch.undo(); }
      },
    }),
    getStudioQrCode: builder.query<string, string>({
      query: (id) => ({
        url:             `studios/${id}/qr`,
        params:          { format: "png" },
        responseHandler: async (response) => URL.createObjectURL(await response.blob()),
      }),
      keepUnusedDataFor: 0,
    }),
    generateReferralCode: builder.mutation<ReferralCodeResponse, string>({
      query: (id) => ({ url: `studios/${id}/referral-codes`, method: "POST" }),
      invalidatesTags: ["Referral"],
    }),
    getReferralCode: builder.query<ReferralCodeResponse | null, string>({
      query: (id) => `studios/${id}/referral-codes`,
      providesTags: ["Referral"],
    }),
    getReferralStats: builder.query<ReferralStatsResponse, string>({
      query: (id) => `studios/${id}/referral-stats`,
      providesTags: ["Referral"],
    }),
  }),
});

export const {
  useRegisterStudioMutation,
  useGetStudioMapQuery,
  useGetMyStudioQuery,
  useUpdateMyStudioMutation,
  useUpdateStudioBrandingMutation,
  useGetStudiosQuery,
  useSuspendStudioMutation,
  useUnsuspendStudioMutation,
  useGetStudioQrCodeQuery,
  useGenerateReferralCodeMutation,
  useGetReferralCodeQuery,
  useGetReferralStatsQuery,
} = studiosApi;
