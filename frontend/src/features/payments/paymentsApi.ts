import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import type {
  PaymentResponse,
  PaymentIntentResponse,
  ClientSecretResponse,
  CreatePaymentIntentRequest,
  UpdateSessionSplitsRequest,
  GetPaymentsParams,
  PaymentCapabilitiesResponse,
} from "./payment.types";

export const paymentsApi = createApi({
  reducerPath: "paymentsApi",
  baseQuery,
  tagTypes: ["Payment"],
  endpoints: (builder) => ({
    getPayments: builder.query<PaymentResponse[], GetPaymentsParams>({
      query: ({ lastSeenId, pageSize = 20 } = {}) => ({
        url:    "payments",
        params: { ...(lastSeenId ? { lastSeenId } : {}), pageSize },
      }),
      providesTags: ["Payment"],
    }),
    getPaymentByAppointment: builder.query<PaymentResponse, string>({
      query: (appointmentId) => `payments/appointment/${appointmentId}`,
      providesTags: (_result, _error, appointmentId) => [
        { type: "Payment" as const, id: `appt:${appointmentId}` },
      ],
    }),
    createPaymentIntent: builder.mutation<PaymentIntentResponse, CreatePaymentIntentRequest>({
      query: (body) => ({ url: "payments", method: "POST", body }),
      invalidatesTags: ["Payment"],
    }),
    declareCashDeposit: builder.mutation<PaymentResponse, { appointmentId: string; note?: string }>({
      query: (body) => ({ url: "payments/cash", method: "POST", body }),
      invalidatesTags: ["Payment"],
    }),
    // Client-facing: create (or resume) the card deposit intent for own appointment
    createDepositPayment: builder.mutation<PaymentIntentResponse, { appointmentId: string }>({
      query: (body) => ({ url: "payments/deposit", method: "POST", body }),
      invalidatesTags: ["Payment"],
    }),
    confirmCashDeposit: builder.mutation<PaymentResponse, string>({
      query: (id) => ({ url: `payments/${id}/cash/confirm`, method: "POST" }),
      // Confirming cash also flips Appointment.DepositStatus server-side, but
      // Appointment lives in a separate RTK Query slice — invalidatesTags here
      // can't reach it, so we invalidate appointmentsApi's cache explicitly.
      async onQueryStarted(_id, { dispatch, queryFulfilled }) {
        try {
          await queryFulfilled;
          dispatch(appointmentsApi.util.invalidateTags(["Appointment"]));
        } catch {
          // Mutation failed — nothing to invalidate.
        }
      },
      invalidatesTags: ["Payment"],
    }),
    captureDeposit: builder.mutation<PaymentResponse, string>({
      query: (id) => ({ url: `payments/${id}/capture`, method: "POST" }),
      invalidatesTags: ["Payment"],
    }),
    refundPayment: builder.mutation<PaymentResponse, { id: string; amount?: number }>({
      query: ({ id, amount }) => ({
        url:    `payments/${id}/refund`,
        method: "POST",
        params: amount !== undefined ? { amount } : {},
      }),
      invalidatesTags: ["Payment"],
    }),
    updateSessionSplits: builder.mutation<PaymentResponse, { id: string; body: UpdateSessionSplitsRequest }>({
      query: ({ id, body }) => ({ url: `payments/${id}/splits`, method: "PUT", body }),
      invalidatesTags: ["Payment"],
    }),
    getPaymentClientSecret: builder.query<ClientSecretResponse, string>({
      query: (id) => `payments/${id}/client-secret`,
    }),
    getPaymentCapabilities: builder.query<PaymentCapabilitiesResponse, void>({
      query: () => "payments/capabilities",
    }),
    downloadInvoice: builder.mutation<Blob, string>({
      query: (id) => ({
        url:             `payments/${id}/invoice`,
        method:          "GET",
        responseHandler: (response) => response.blob(),
      }),
    }),
  }),
});

export const {
  useGetPaymentsQuery,
  useGetPaymentByAppointmentQuery,
  useCreatePaymentIntentMutation,
  useDeclareCashDepositMutation,
  useCreateDepositPaymentMutation,
  useConfirmCashDepositMutation,
  useCaptureDepositMutation,
  useRefundPaymentMutation,
  useUpdateSessionSplitsMutation,
  useGetPaymentClientSecretQuery,
  useDownloadInvoiceMutation,
  useGetPaymentCapabilitiesQuery,
} = paymentsApi;
