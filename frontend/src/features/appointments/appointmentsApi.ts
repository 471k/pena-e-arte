import { createApi, fetchBaseQuery } from "@reduxjs/toolkit/query/react";
import type { RootState } from "@/app/store";
import type {
  AppointmentResponse,
  CreateAppointmentRequest,
  GetAppointmentsParams,
} from "./appointment.types";

export const appointmentsApi = createApi({
  reducerPath: "appointmentsApi",
  baseQuery: fetchBaseQuery({
    baseUrl: "/api/v1/",
    prepareHeaders: (headers, { getState }) => {
      const { token, tenantId } = (getState() as RootState).auth;
      if (token)    headers.set("Authorization", `Bearer ${token}`);
      if (tenantId) headers.set("X-Tenant-Id", tenantId);
      return headers;
    },
  }),
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
