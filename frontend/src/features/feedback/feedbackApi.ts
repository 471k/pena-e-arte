import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  FeedbackReportResponse,
  SubmitFeedbackRequest,
  UpdateFeedbackStatusRequest,
} from "./feedback.types";

export const feedbackApi = createApi({
  reducerPath: "feedbackApi",
  baseQuery,
  tagTypes: ["Feedback"],
  endpoints: (builder) => ({
    submitFeedback: builder.mutation<FeedbackReportResponse, SubmitFeedbackRequest>({
      query: (body) => ({ url: "feedback", method: "POST", body }),
    }),
    getFeedbackReports: builder.query<
      FeedbackReportResponse[],
      { type?: string; status?: string } | void
    >({
      query: (args) => {
        const { type, status } = args ?? {};
        const params = new URLSearchParams();
        if (type)   params.set("type",   type);
        if (status) params.set("status", status);
        const query = params.toString();
        return `platform/feedback${query ? `?${query}` : ""}`;
      },
      providesTags: ["Feedback"],
    }),
    updateFeedbackStatus: builder.mutation<
      FeedbackReportResponse,
      { id: string } & UpdateFeedbackStatusRequest
    >({
      query: ({ id, ...body }) => ({
        url:    `platform/feedback/${id}/status`,
        method: "PATCH",
        body,
      }),
      invalidatesTags: ["Feedback"],
    }),
  }),
});

export const {
  useSubmitFeedbackMutation,
  useGetFeedbackReportsQuery,
  useUpdateFeedbackStatusMutation,
} = feedbackApi;
