import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
// paymentsApi imports this slice too; the cycle is safe because each side only uses the other
// inside callbacks that run after both modules have loaded.
import { paymentsApi } from "@/features/payments/paymentsApi";
import type {
  AppointmentCommunicationResponse,
  AppointmentPreparationResponse,
  AppointmentResponse,
  AssignAppointmentArtistRequest,
  CancellationPreviewResponse,
  CheckSlotAvailabilityParams,
  ClientAppointmentDetailResponse,
  CreateAppointmentRequest,
  GetAppointmentsParams,
  NoShowPreviewResponse,
  NoShowResultResponse,
  RescheduleAppointmentRequest,
  ReschedulePreviewResponse,
  SlotAvailabilityResponse,
} from "./appointment.types";
import type { OwnerExceptionsResponse } from "@/features/dashboard/dashboard.types";

export const appointmentsApi = createApi({
  reducerPath: "appointmentsApi",
  baseQuery,
  tagTypes: ["Appointment"],
  endpoints: (builder) => ({
    getAppointments: builder.query<AppointmentResponse[], GetAppointmentsParams>({
      query: ({ from, to, artistId, assignedToMe } = {}) => ({
        url:    "appointments",
        params: {
          ...(from ? { from } : {}),
          ...(to ? { to } : {}),
          ...(artistId ? { artistId } : {}),
          ...(assignedToMe ? { assignedToMe: true } : {}),
        },
      }),
      providesTags: ["Appointment"],
    }),
    // Staff: a session's preparation for the artist (E04a), worded about the client. Tagged
    // "Appointment" so a cancel, reschedule or completion anywhere refetches it.
    getAppointmentPreparation: builder.query<AppointmentPreparationResponse, string>({
      query: (id) => `appointments/${id}/preparation`,
      providesTags: ["Appointment"],
    }),
    // Owner home (E05a, O01): what needs the owner, with real counts. Tagged "Appointment" so an
    // assignment, confirmation, cancellation or cash confirmation (which invalidates this slice)
    // refreshes it.
    getOwnerExceptions: builder.query<OwnerExceptionsResponse, void>({
      query: () => "dashboard/exceptions",
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
    // Client-facing: one of the caller's own appointments, for the client appointment page.
    // Tagged "Appointment" so a cancel / reschedule from the page refetches it and shows the
    // server's actual result.
    getMyAppointment: builder.query<ClientAppointmentDetailResponse, string>({
      query: (id) => `appointments/mine/${id}`,
      providesTags: ["Appointment"],
    }),
    // Advisory only: what cancelling would do with the deposit, worked out by the server. Never
    // cached between dialog openings (keepUnusedDataFor: 0) — the studio's rule or the payment can
    // change, and DELETE re-decides at commit anyway.
    getCancellationPreview: builder.query<CancellationPreviewResponse, string>({
      query: (id) => `appointments/${id}/cancellation-preview`,
      keepUnusedDataFor: 0,
    }),
    createAppointment: builder.mutation<AppointmentResponse, CreateAppointmentRequest>({
      query: (body) => ({ url: "appointments", method: "POST", body }),
      invalidatesTags: ["Appointment"],
    }),
    cancelAppointment: builder.mutation<void, string>({
      query: (id) => ({ url: `appointments/${id}`, method: "DELETE" }),
      // Cancelling can refund or forfeit the deposit server-side, but Payment lives in a
      // separate RTK Query slice — without this, My bookings kept showing "Deposit paid" on a
      // cancelled, refunded booking until a reload.
      async onQueryStarted(_id, { dispatch, queryFulfilled }) {
        try {
          await queryFulfilled;
          dispatch(paymentsApi.util.invalidateTags(["Payment"]));
        } catch {
          // Mutation failed — nothing to invalidate.
        }
      },
      invalidatesTags: ["Appointment"],
    }),
    // Staff answer to a booking request: the client is told the studio can't take it (and any
    // deposit is refunded), unlike a cancellation of an accepted booking.
    declineAppointment: builder.mutation<void, string>({
      query: (id) => ({ url: `appointments/${id}/decline`, method: "PATCH" }),
      async onQueryStarted(_id, { dispatch, queryFulfilled }) {
        try {
          await queryFulfilled;
          dispatch(paymentsApi.util.invalidateTags(["Payment"]));
        } catch {
          // Mutation failed — nothing to invalidate.
        }
      },
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
    // Returns what the commit actually did to the deposit (E04b). A no-show can capture a held deposit
    // or change the payment row, so the payment screens refetch too.
    markNoShow: builder.mutation<NoShowResultResponse, string>({
      query: (id) => ({ url: `appointments/${id}/no-show`, method: "PATCH" }),
      async onQueryStarted(_id, { dispatch, queryFulfilled }) {
        try {
          await queryFulfilled;
          dispatch(paymentsApi.util.invalidateTags(["Payment"]));
        } catch {
          // Mutation failed — nothing to invalidate.
        }
      },
      invalidatesTags: ["Appointment"],
    }),
    // Whether the client has been told about a reschedule, and whether aftercare went out (E04b, A08).
    // Tagged "Appointment" so a reschedule or completion anywhere refetches it.
    getAppointmentCommunication: builder.query<AppointmentCommunicationResponse, string>({
      query: (id) => `appointments/${id}/communication`,
      providesTags: ["Appointment"],
    }),
    // What a no-show would do to the deposit, decided by the server (E04b, A04). Never cached between
    // openings: the payment can change, and the commit decides again anyway.
    getNoShowPreview: builder.query<NoShowPreviewResponse, string>({
      query: (id) => `appointments/${id}/no-show-preview`,
      keepUnusedDataFor: 0,
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
      query: ({ artistId, date, durationMinutes, excludeAppointmentId }) => ({
        url:    "appointments/check-slot",
        params: {
          ...(artistId ? { artistId } : {}),
          date,
          durationMinutes,
          ...(excludeAppointmentId ? { excludeAppointmentId } : {}),
        },
      }),
      keepUnusedDataFor: 0,
    }),
    // Whether the appointment can be moved at all (E04b, A02), by the save's own rule. Never cached
    // between dialog openings: the notice window moves with the clock.
    getReschedulePreview: builder.query<ReschedulePreviewResponse, string>({
      query: (id) => `appointments/${id}/reschedule-preview`,
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
  useGetOwnerExceptionsQuery,
  useGetAppointmentQuery,
  useGetAppointmentPreparationQuery,
  useGetMyAppointmentsQuery,
  useGetMyAppointmentQuery,
  useGetCancellationPreviewQuery,
  useCreateAppointmentMutation,
  useCancelAppointmentMutation,
  useDeclineAppointmentMutation,
  useConfirmAppointmentMutation,
  useCompleteAppointmentMutation,
  useMarkNoShowMutation,
  useGetNoShowPreviewQuery,
  useGetAppointmentCommunicationQuery,
  useRescheduleAppointmentMutation,
  useCheckSlotAvailabilityQuery,
  useGetReschedulePreviewQuery,
  useAssignAppointmentArtistMutation,
} = appointmentsApi;
