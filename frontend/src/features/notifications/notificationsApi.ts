import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  NotificationLogResponse,
  NotificationPreferenceItem,
  NotificationPreferencesResponse,
  NotificationsFilter,
} from "./notification.types";

export const notificationsApi = createApi({
  reducerPath: "notificationsApi",
  baseQuery,
  tagTypes: ["NotificationLog", "NotificationPreferences"],
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
    getNotificationPreferences: builder.query<NotificationPreferencesResponse, void>({
      query: () => "notifications/preferences",
      providesTags: ["NotificationPreferences"],
    }),
    updateNotificationPreferences: builder.mutation<void, NotificationPreferenceItem[]>({
      query: (preferences) => ({
        url:    "notifications/preferences",
        method: "PUT",
        body:   { preferences },
      }),
      invalidatesTags: ["NotificationPreferences"],
    }),
  }),
});

export const {
  useGetNotificationsQuery,
  useGetNotificationPreferencesQuery,
  useUpdateNotificationPreferencesMutation,
} = notificationsApi;
