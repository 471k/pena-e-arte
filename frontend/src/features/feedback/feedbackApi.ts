import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  FeedbackReportResponse,
  SubmitFeedbackRequest,
  UpdateFeedbackStatusRequest,
  FeedbackMessageResponse,
  PostFeedbackMessageRequest,
} from "./feedback.types";

export const feedbackApi = createApi({
  reducerPath: "feedbackApi",
  baseQuery,
  tagTypes: ["Feedback", "FeedbackMessage"],
  endpoints: (builder) => ({
    submitFeedback: builder.mutation<FeedbackReportResponse, SubmitFeedbackRequest>({
      query: (body) => ({ url: "feedback", method: "POST", body }),
      invalidatesTags: ["Feedback"],
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
    getMyFeedbackReports: builder.query<FeedbackReportResponse[], { type?: string } | void>({
      query: (args) => `feedback/mine${args?.type ? `?type=${args.type}` : ""}`,
      providesTags: ["Feedback"],
    }),
    getFeedbackMessages: builder.query<FeedbackMessageResponse[], string>({
      query: (feedbackReportId) => `feedback/${feedbackReportId}/messages`,
      providesTags: (_result, _err, id) => [{ type: "FeedbackMessage", id }],
    }),
    postFeedbackMessage: builder.mutation<
      FeedbackMessageResponse,
      { feedbackReportId: string; mayReopen?: boolean } & PostFeedbackMessageRequest
    >({
      query: ({ feedbackReportId, body }) => ({
        url:    `feedback/${feedbackReportId}/messages`,
        method: "POST",
        body: { body },
      }),
      // "Feedback" backs the report-list queries (admin inbox, client's own tickets) —
      // only worth invalidating when this reply could actually change a report-level field.
      // Mirrors PostFeedbackMessageHandler's own reopen condition: a studio-side reply on an
      // already-closed ticket reopens it; anything else (including any admin reply) leaves
      // the report row unchanged, so refetching the whole list would be wasted work.
      invalidatesTags: (_result, _err, { feedbackReportId, mayReopen }) => [
        { type: "FeedbackMessage", id: feedbackReportId },
        ...(mayReopen ? (["Feedback"] as const) : []),
      ],
    }),
  }),
});

export const {
  useSubmitFeedbackMutation,
  useGetFeedbackReportsQuery,
  useUpdateFeedbackStatusMutation,
  useGetMyFeedbackReportsQuery,
  useGetFeedbackMessagesQuery,
  usePostFeedbackMessageMutation,
} = feedbackApi;
