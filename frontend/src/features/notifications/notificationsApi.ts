import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type { NotificationLogResponse, NotificationsFilter } from "./notification.types";

export const notificationsApi = createApi({
  reducerPath: "notificationsApi",
  baseQuery,
  tagTypes: ["NotificationLog"],
  endpoints: (builder) => ({
    getNotifications: builder.query<NotificationLogResponse[], NotificationsFilter>({
      query: ({ recipientId, channel, from, to } = {}) => {
        const params = new URLSearchParams();
        if (recipientId) params.set("recipientId", recipientId);
        if (channel)     params.set("channel",     channel);
        if (from)        params.set("from",        from);
        if (to)          params.set("to",          to);
        const qs = params.toString();
        return qs ? `notifications?${qs}` : "notifications";
      },
      providesTags: ["NotificationLog"],
    }),
  }),
});

export const { useGetNotificationsQuery } = notificationsApi;
