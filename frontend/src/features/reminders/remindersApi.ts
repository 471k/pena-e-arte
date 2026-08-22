import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  CreateManualReminderRequest,
  GetManualRemindersParams,
  ManualReminderResponse,
} from "./reminder.types";

export const remindersApi = createApi({
  reducerPath: "remindersApi",
  baseQuery,
  tagTypes: ["ManualReminder"],
  endpoints: (builder) => ({
    getManualReminders: builder.query<ManualReminderResponse[], GetManualRemindersParams>({
      query: ({ appointmentId, clientId } = {}) => {
        const params = new URLSearchParams();
        if (appointmentId) params.set("appointmentId", appointmentId);
        if (clientId)      params.set("clientId",      clientId);
        return `reminders?${params.toString()}`;
      },
      providesTags: ["ManualReminder"],
    }),
    createManualReminder: builder.mutation<ManualReminderResponse, CreateManualReminderRequest>({
      query: (body) => ({ url: "reminders", method: "POST", body }),
      invalidatesTags: ["ManualReminder"],
    }),
    cancelManualReminder: builder.mutation<void, string>({
      query: (id) => ({ url: `reminders/${id}`, method: "DELETE" }),
      invalidatesTags: ["ManualReminder"],
    }),
  }),
});

export const {
  useGetManualRemindersQuery,
  useCreateManualReminderMutation,
  useCancelManualReminderMutation,
} = remindersApi;
