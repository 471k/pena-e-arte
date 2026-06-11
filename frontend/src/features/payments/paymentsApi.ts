import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  PaymentResponse,
  PaymentIntentResponse,
  ClientSecretResponse,
  CreatePaymentIntentRequest,
  UpdateSessionSplitsRequest,
  GetPaymentsParams,
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
  }),
});

export const {
  useGetPaymentsQuery,
  useGetPaymentByAppointmentQuery,
  useCreatePaymentIntentMutation,
  useCaptureDepositMutation,
  useRefundPaymentMutation,
  useUpdateSessionSplitsMutation,
  useGetPaymentClientSecretQuery,
} = paymentsApi;
