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
  slugLockedAt:         string | null;
  phoneNumber:          string | null;
  instagramHandle:      string | null;
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
  code:                   string | null;
  redemptionCount:        number;
  discountsApplied:       number;
  referrerRewardsApplied: number;
}

export interface UpdateStudioRequest {
  name:            string;
  city:            string;
  latitude:        number;
  longitude:       number;
  phoneNumber?:    string | null;
  instagramHandle?: string | null;
}

export interface StudioClosureResponse {
  id:        string;
  startDate: string;
  endDate:   string;
  reason:    string;
}

export interface AddStudioClosureRequest {
  startDate: string;
  endDate:   string;
  reason:    string;
}

export const studiosApi = createApi({
  reducerPath: "studiosApi",
  baseQuery,
  tagTypes: ["Studio", "Referral", "StudioClosure"],
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
    // Issuer: get single studio by id
    getStudioById: builder.query<StudioResponse, string>({
      query: (id) => `studios/${id}`,
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
    getStudioQrCode: builder.query<string, { id: string; format?: "png" | "svg" }>({
      query: ({ id, format = "png" }) => ({
        url:             `studios/${id}/qr`,
        params:          { format },
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
    updateStudioSlug: builder.mutation<void, { id: string; newSlug: string }>({
      query: ({ id, newSlug }) => ({
        url:    `studios/${id}/slug`,
        method: "PATCH",
        body:   { newSlug },
      }),
      invalidatesTags: ["Studio"],
    }),
    getStudioClosures: builder.query<StudioClosureResponse[], string>({
      query: (id) => `studios/${id}/closures`,
      providesTags: ["StudioClosure"],
    }),
    addStudioClosure: builder.mutation<{ id: string }, { id: string; body: AddStudioClosureRequest }>({
      query: ({ id, body }) => ({
        url:    `studios/${id}/closures`,
        method: "POST",
        body,
      }),
      invalidatesTags: ["StudioClosure"],
    }),
    deleteStudioClosure: builder.mutation<void, { id: string; closureId: string }>({
      query: ({ id, closureId }) => ({
        url:    `studios/${id}/closures/${closureId}`,
        method: "DELETE",
      }),
      invalidatesTags: ["StudioClosure"],
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
  useGetStudioByIdQuery,
  useSuspendStudioMutation,
  useUnsuspendStudioMutation,
  useGetStudioQrCodeQuery,
  useLazyGetStudioQrCodeQuery,
  useGenerateReferralCodeMutation,
  useGetReferralCodeQuery,
  useGetReferralStatsQuery,
  useUpdateStudioSlugMutation,
  useGetStudioClosuresQuery,
  useAddStudioClosureMutation,
  useDeleteStudioClosureMutation,
} = studiosApi;
