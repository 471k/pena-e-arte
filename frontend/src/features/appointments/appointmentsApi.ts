import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  AppointmentResponse,
  CreateAppointmentRequest,
  GetAppointmentsParams,
} from "./appointment.types";

export const appointmentsApi = createApi({
  reducerPath: "appointmentsApi",
  baseQuery,
  tagTypes: ["Appointment"],
  endpoints: (builder) => ({
    getAppointments: builder.query<AppointmentResponse[], GetAppointmentsParams>({
      query: ({ from, to } = {}) => ({
        url:    "appointments",
        params: { ...(from ? { from } : {}), ...(to ? { to } : {}) },
      }),
      providesTags: ["Appointment"],
    }),
    createAppointment: builder.mutation<AppointmentResponse, CreateAppointmentRequest>({
      query: (body) => ({ url: "appointments", method: "POST", body }),
      invalidatesTags: ["Appointment"],
    }),
    cancelAppointment: builder.mutation<void, string>({
      query: (id) => ({ url: `appointments/${id}`, method: "DELETE" }),
      invalidatesTags: ["Appointment"],
    }),
  }),
});

export const {
  useGetAppointmentsQuery,
  useCreateAppointmentMutation,
  useCancelAppointmentMutation,
} = appointmentsApi;
