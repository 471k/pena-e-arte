import { createApi, fetchBaseQuery } from "@reduxjs/toolkit/query/react";
import type { RootState } from "@/app/store";

export interface IndustryReportSummary {
  period:      string;
  generatedAt: string;
  downloadUrl: string;
}

export const platformApi = createApi({
  reducerPath: "platformApi",
  baseQuery: fetchBaseQuery({
    baseUrl: "/api/v1/",
    prepareHeaders: (headers, { getState }) => {
      const { token, tenantId } = (getState() as RootState).auth;
      if (token)    headers.set("Authorization", `Bearer ${token}`);
      if (tenantId) headers.set("X-Tenant-Id", tenantId);
      return headers;
    },
  }),
  tagTypes: ["IndustryReport"],
  endpoints: (builder) => ({
    getIndustryReports: builder.query<IndustryReportSummary[], void>({
      query: () => "platform/reports/industry",
      providesTags: ["IndustryReport"],
    }),
  }),
});

export const { useGetIndustryReportsQuery } = platformApi;
