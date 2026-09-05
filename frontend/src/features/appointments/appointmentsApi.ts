import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  AppointmentResponse,
  AssignAppointmentArtistRequest,
  CheckSlotAvailabilityParams,
  CreateAppointmentRequest,
  GetAppointmentsParams,
  RescheduleAppointmentRequest,
  SlotAvailabilityResponse,
} from "./appointment.types";

export const appointmentsApi = createApi({
  reducerPath: "appointmentsApi",
  baseQuery,
  tagTypes: ["Appointment"],
  endpoints: (builder) => ({
    getAppointments: builder.query<AppointmentResponse[], GetAppointmentsParams>({
      query: ({ from, to, artistId } = {}) => ({
        url:    "appointments",
        params: {
          ...(from ? { from } : {}),
          ...(to ? { to } : {}),
          ...(artistId ? { artistId } : {}),
        },
      }),
      providesTags: ["Appointment"],
    }),
    getAppointment: builder.query<AppointmentResponse, string>({
      query: (id) => `appointments/${id}`,
      providesTags: ["Appointment"],
    }),
    // Client-facing: the caller's own appointments only
    getMyAppointments: builder.query<AppointmentResponse[], void>({
      query: () => "appointments/mine",
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
    confirmAppointment: builder.mutation<AppointmentResponse, string>({
      query: (id) => ({ url: `appointments/${id}/confirm`, method: "PATCH" }),
      invalidatesTags: ["Appointment"],
    }),
    completeAppointment: builder.mutation<AppointmentResponse, string>({
      query: (id) => ({ url: `appointments/${id}/complete`, method: "PATCH" }),
      invalidatesTags: ["Appointment"],
    }),
    markNoShow: builder.mutation<AppointmentResponse, string>({
      query: (id) => ({ url: `appointments/${id}/no-show`, method: "PATCH" }),
      invalidatesTags: ["Appointment"],
    }),
    rescheduleAppointment: builder.mutation<
      AppointmentResponse,
      { id: string } & RescheduleAppointmentRequest
    >({
      query: ({ id, ...body }) => ({
        url:    `appointments/${id}/reschedule`,
        method: "PATCH",
        body,
      }),
      invalidatesTags: ["Appointment"],
    }),
    checkSlotAvailability: builder.query<SlotAvailabilityResponse, CheckSlotAvailabilityParams>({
      query: ({ artistId, date, durationMinutes }) => ({
        url:    "appointments/check-slot",
        params: { ...(artistId ? { artistId } : {}), date, durationMinutes },
      }),
      keepUnusedDataFor: 0,
    }),
    assignAppointmentArtist: builder.mutation<
      AppointmentResponse,
      { id: string; body: AssignAppointmentArtistRequest }
    >({
      query: ({ id, body }) => ({
        url:    `appointments/${id}/artist`,
        method: "PATCH",
        body,
      }),
      invalidatesTags: ["Appointment"],
    }),
  }),
});

export const {
  useGetAppointmentsQuery,
  useGetAppointmentQuery,
  useGetMyAppointmentsQuery,
  useCreateAppointmentMutation,
  useCancelAppointmentMutation,
  useConfirmAppointmentMutation,
  useCompleteAppointmentMutation,
  useMarkNoShowMutation,
  useRescheduleAppointmentMutation,
  useCheckSlotAvailabilityQuery,
  useAssignAppointmentArtistMutation,
} = appointmentsApi;
