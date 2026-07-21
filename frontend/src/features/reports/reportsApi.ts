import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type { RevenueSummaryResponse } from "./report.types";

export const reportsApi = createApi({
  reducerPath: "reportsApi",
  baseQuery,
  tagTypes: ["RevenueSummary"],
  endpoints: (builder) => ({
    getRevenueSummary: builder.query<RevenueSummaryResponse, void>({
      query: () => "reports/revenue-summary",
      providesTags: ["RevenueSummary"],
    }),
  }),
});

export const { useGetRevenueSummaryQuery } = reportsApi;
