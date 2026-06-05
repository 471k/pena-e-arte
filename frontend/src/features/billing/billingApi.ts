import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type { SubscriptionResponse, PlanResponse, CreateSubscriptionRequest } from "./billing.types";

export const billingApi = createApi({
  reducerPath: "billingApi",
  baseQuery,
  tagTypes: ["Subscription"],
  endpoints: (builder) => ({
    getPlans: builder.query<PlanResponse[], void>({
      query: () => "billing/plans",
    }),
    getSubscription: builder.query<SubscriptionResponse, void>({
      query: () => "billing/subscription",
      providesTags: ["Subscription"],
    }),
    createSubscription: builder.mutation<SubscriptionResponse, CreateSubscriptionRequest>({
      query: (body) => ({ url: "billing/subscription", method: "POST", body }),
      invalidatesTags: ["Subscription"],
    }),
  }),
});

export const {
  useGetPlansQuery,
  useGetSubscriptionQuery,
  useCreateSubscriptionMutation,
} = billingApi;
