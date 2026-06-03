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
  }),
});

export const { useRegisterStudioMutation } = studiosApi;
