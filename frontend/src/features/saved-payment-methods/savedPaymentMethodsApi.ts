import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  SavedPaymentMethodResponse,
  AddSavedPaymentMethodRequest,
} from "./savedPaymentMethod.types";

export const savedPaymentMethodsApi = createApi({
  reducerPath: "savedPaymentMethodsApi",
  baseQuery,
  tagTypes: ["SavedPaymentMethod"],
  endpoints: (builder) => ({
    getSavedPaymentMethods: builder.query<SavedPaymentMethodResponse[], void>({
      query: () => "saved-payment-methods",
      providesTags: ["SavedPaymentMethod"],
    }),
    addSavedPaymentMethod: builder.mutation<SavedPaymentMethodResponse, AddSavedPaymentMethodRequest>({
      query: (body) => ({ url: "saved-payment-methods", method: "POST", body }),
      invalidatesTags: ["SavedPaymentMethod"],
    }),
    deleteSavedPaymentMethod: builder.mutation<void, string>({
      query: (id) => ({ url: `saved-payment-methods/${id}`, method: "DELETE" }),
      invalidatesTags: ["SavedPaymentMethod"],
    }),
  }),
});

export const {
  useGetSavedPaymentMethodsQuery,
  useAddSavedPaymentMethodMutation,
  useDeleteSavedPaymentMethodMutation,
} = savedPaymentMethodsApi;
