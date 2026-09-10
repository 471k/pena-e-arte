import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  PromoCodeResponse,
  CreatePromoCodeRequest,
  UpdatePromoCodeRequest,
} from "./promoCode.types";

export const promoCodesApi = createApi({
  reducerPath: "promoCodesApi",
  baseQuery,
  tagTypes: ["PromoCode"],
  endpoints: (builder) => ({
    getPromoCodes: builder.query<PromoCodeResponse[], void>({
      query: () => "promo-codes",
      providesTags: ["PromoCode"],
    }),
    getPromoCodeById: builder.query<PromoCodeResponse, string>({
      query: (id) => `promo-codes/${id}`,
      providesTags: (_result, _error, id) => [{ type: "PromoCode", id }],
    }),
    createPromoCode: builder.mutation<PromoCodeResponse, CreatePromoCodeRequest>({
      query: (body) => ({ url: "promo-codes", method: "POST", body }),
      invalidatesTags: ["PromoCode"],
    }),
    updatePromoCode: builder.mutation<PromoCodeResponse, { id: string; body: UpdatePromoCodeRequest }>({
      query: ({ id, body }) => ({ url: `promo-codes/${id}`, method: "PUT", body }),
      invalidatesTags: (_result, _error, { id }) => [{ type: "PromoCode", id }, "PromoCode"],
    }),
    deletePromoCode: builder.mutation<void, string>({
      query: (id) => ({ url: `promo-codes/${id}`, method: "DELETE" }),
      invalidatesTags: ["PromoCode"],
    }),
  }),
});

export const {
  useGetPromoCodesQuery,
  useGetPromoCodeByIdQuery,
  useCreatePromoCodeMutation,
  useUpdatePromoCodeMutation,
  useDeletePromoCodeMutation,
} = promoCodesApi;
