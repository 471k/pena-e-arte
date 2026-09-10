import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  GiftCardResponse, GiftCardBalanceResponse, PurchaseGiftCardRequest, PurchaseGiftCardResponse,
  RedeemGiftCardRequest,
} from "./giftCards.types";

export const giftCardsApi = createApi({
  reducerPath: "giftCardsApi",
  baseQuery,
  tagTypes: ["GiftCard"],
  endpoints: (builder) => ({
    purchaseGiftCard: builder.mutation<PurchaseGiftCardResponse, PurchaseGiftCardRequest>({
      query: (body) => ({ url: "gift-cards", method: "POST", body }),
    }),
    getGiftCardBalance: builder.query<GiftCardBalanceResponse, string>({
      query: (code) => `gift-cards/${code}/balance`,
    }),
    redeemGiftCard: builder.mutation<GiftCardResponse, RedeemGiftCardRequest>({
      query: (body) => ({ url: "gift-cards/redeem", method: "POST", body }),
      invalidatesTags: ["GiftCard"],
    }),
    getGiftCards: builder.query<GiftCardResponse[], void>({
      query: () => "gift-cards",
      providesTags: ["GiftCard"],
    }),
    voidGiftCard: builder.mutation<GiftCardResponse, string>({
      query: (id) => ({ url: `gift-cards/${id}/void`, method: "POST" }),
      invalidatesTags: ["GiftCard"],
    }),
  }),
});

export const {
  usePurchaseGiftCardMutation,
  useLazyGetGiftCardBalanceQuery,
  useRedeemGiftCardMutation,
  useGetGiftCardsQuery,
  useVoidGiftCardMutation,
} = giftCardsApi;
