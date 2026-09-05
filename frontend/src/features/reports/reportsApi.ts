import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type { ArtistEarningsResponse, RevenueSummaryResponse } from "./report.types";

export const reportsApi = createApi({
  reducerPath: "reportsApi",
  baseQuery,
  tagTypes: ["RevenueSummary", "MyEarnings"],
  endpoints: (builder) => ({
    getRevenueSummary: builder.query<RevenueSummaryResponse, void>({
      query: () => "reports/revenue-summary",
      providesTags: ["RevenueSummary"],
    }),
    getMyEarnings: builder.query<ArtistEarningsResponse, void>({
      query: () => "reports/my-earnings",
      providesTags: ["MyEarnings"],
    }),
  }),
});

export const { useGetRevenueSummaryQuery, useGetMyEarningsQuery } = reportsApi;
