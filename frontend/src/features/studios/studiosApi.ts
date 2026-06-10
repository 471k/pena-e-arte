import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";

export interface RegisterStudioRequest {
  name: string;
  slug: string;
  city: string;
  latitude: number;
  longitude: number;
  ownerEmail: string;
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
}

export interface StudioMapItem {
  id: string;
  name: string;
  slug: string;
  latitude: number;
  longitude: number;
  city: string;
}

export interface ConnectStudioRequest {
  returnUrl:  string;
  refreshUrl: string;
  country:    string;
}

export interface ConnectOnboardingResponse {
  onboardingUrl: string;
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
  tagTypes: ["Studio"],
  endpoints: (builder) => ({
    registerStudio: builder.mutation<StudioResponse, RegisterStudioRequest>({
      query: (body) => ({ url: "studios", method: "POST", body }),
    }),
    getStudioMap: builder.query<StudioMapItem[], void>({
      query: () => "studios/map",
    }),
    connectStudio: builder.mutation<ConnectOnboardingResponse, ConnectStudioRequest>({
      query: (body) => ({ url: "studios/connect", method: "POST", body }),
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
    }),
    unsuspendStudio: builder.mutation<void, string>({
      query: (id) => ({ url: `studios/${id}/unsuspend`, method: "PATCH" }),
      invalidatesTags: ["Studio"],
    }),
  }),
});

export const {
  useRegisterStudioMutation,
  useGetStudioMapQuery,
  useConnectStudioMutation,
  useGetMyStudioQuery,
  useUpdateMyStudioMutation,
  useUpdateStudioBrandingMutation,
  useGetStudiosQuery,
  useSuspendStudioMutation,
  useUnsuspendStudioMutation,
} = studiosApi;
