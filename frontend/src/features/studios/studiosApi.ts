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
  id: string;
  name: string;
  slug: string;
  city: string;
  latitude: number;
  longitude: number;
  trialExpiresAt: string;
  createdAt: string;
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

export const studiosApi = createApi({
  reducerPath: "studiosApi",
  baseQuery,
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
  }),
});

export const {
  useRegisterStudioMutation,
  useGetStudioMapQuery,
  useConnectStudioMutation,
} = studiosApi;
