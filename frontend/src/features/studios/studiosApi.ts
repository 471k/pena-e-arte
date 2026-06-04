import { createApi, fetchBaseQuery } from "@reduxjs/toolkit/query/react";
import type { RootState } from "@/app/store";

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

export const studiosApi = createApi({
  reducerPath: "studiosApi",
  baseQuery: fetchBaseQuery({
    baseUrl: "/api/v1/",
    prepareHeaders: (headers, { getState }) => {
      const { token, tenantId } = (getState() as RootState).auth;
      if (token) headers.set("Authorization", `Bearer ${token}`);
      if (tenantId) headers.set("X-Tenant-Id", tenantId);
      return headers;
    },
  }),
  endpoints: (builder) => ({
    registerStudio: builder.mutation<StudioResponse, RegisterStudioRequest>({
      query: (body) => ({ url: "studios", method: "POST", body }),
    }),
    getStudioMap: builder.query<StudioMapItem[], void>({
      query: () => "studios/map",
    }),
  }),
});

export const { useRegisterStudioMutation, useGetStudioMapQuery } = studiosApi;
