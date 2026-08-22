import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type { ConductReportResponse } from "./conductReports.types";

interface UpdateConductReportStatusArgs {
  id:             string;
  status:         string;
  resolutionNote?: string;
}

export const conductReportsApi = createApi({
  reducerPath: "conductReportsApi",
  baseQuery,
  tagTypes: ["ConductReport"],
  endpoints: (builder) => ({
    getMyStudioConductReports: builder.query<ConductReportResponse[], { status?: string } | void>({
      query: (args) => {
        const status = args?.status;
        return `studios/me/conduct-reports${status ? `?status=${status}` : ""}`;
      },
      providesTags: ["ConductReport"],
    }),
    getMyConductReportsAsArtist: builder.query<ConductReportResponse[], void>({
      query: () => "artists/me/conduct-reports",
      providesTags: ["ConductReport"],
    }),
    getPlatformConductReports: builder.query<
      ConductReportResponse[],
      { category?: string; status?: string; studioId?: string } | void
    >({
      query: (args) => {
        const params = new URLSearchParams();
        if (args?.category) params.set("category", args.category);
        if (args?.status)   params.set("status",   args.status);
        if (args?.studioId) params.set("studioId", args.studioId);
        const query = params.toString();
        return `platform/conduct-reports${query ? `?${query}` : ""}`;
      },
      providesTags: ["ConductReport"],
    }),
    updateConductReportStatus: builder.mutation<void, UpdateConductReportStatusArgs>({
      query: ({ id, ...body }) => ({
        url:    `conduct-reports/${id}/status`,
        method: "PATCH",
        body,
      }),
      invalidatesTags: ["ConductReport"],
    }),
  }),
});

export const {
  useGetMyStudioConductReportsQuery,
  useGetMyConductReportsAsArtistQuery,
  useGetPlatformConductReportsQuery,
  useUpdateConductReportStatusMutation,
} = conductReportsApi;
