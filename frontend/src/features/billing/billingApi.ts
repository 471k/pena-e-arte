import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type { SubscriptionResponse, PlanResponse, CreateSubscriptionRequest } from "./billing.types";

export interface CreatePlanRequest {
  name:                 string;
  billingInterval:      string;
  priceMonthly:         number;
  priceYearly:          number;
  yearlyDiscountPercent: number;
}

export interface UpdatePlanRequest {
  name:                  string;
  priceMonthly:          number;
  priceYearly:           number;
  yearlyDiscountPercent: number;
  allowBrandingRemoval:  boolean;
}

export const billingApi = createApi({
  reducerPath: "billingApi",
  baseQuery,
  tagTypes: ["Subscription", "Plan"],
  endpoints: (builder) => ({
    getPlans: builder.query<PlanResponse[], void>({
      query: () => "billing/plans",
      providesTags: ["Plan"],
    }),
    getSubscription: builder.query<SubscriptionResponse, void>({
      query: () => "billing/subscription",
      providesTags: ["Subscription"],
    }),
    createSubscription: builder.mutation<SubscriptionResponse, CreateSubscriptionRequest>({
      query: (body) => ({ url: "billing/subscription", method: "POST", body }),
      invalidatesTags: ["Subscription"],
    }),
    // Card subscribe via Stripe-hosted Checkout — returns a URL to redirect to.
    createCheckout: builder.mutation<
      { url: string },
      { planId: string; successUrl: string; cancelUrl: string }
    >({
      query: (body) => ({ url: "billing/subscription/checkout", method: "POST", body }),
    }),
    // Reconcile after returning from Checkout (in case the webhook was missed).
    finalizeCheckout: builder.mutation<SubscriptionResponse | null, { sessionId: string }>({
      query: (body) => ({ url: "billing/subscription/checkout/finalize", method: "POST", body }),
      invalidatesTags: ["Subscription"],
    }),
    // Plan switching: upgrades apply immediately (prorated), downgrades at period end
    changePlan: builder.mutation<SubscriptionResponse, { planId: string }>({
      query: (body) => ({ url: "billing/subscription/plan", method: "PUT", body }),
      invalidatesTags: ["Subscription"],
    }),
    cancelPlanChange: builder.mutation<SubscriptionResponse, void>({
      query: () => ({ url: "billing/subscription/plan/pending", method: "DELETE" }),
      invalidatesTags: ["Subscription"],
    }),
    // Issuer plan management
    getIssuerPlans: builder.query<PlanResponse[], void>({
      query: () => "billing/plans",
      providesTags: ["Plan"],
    }),
    createPlan: builder.mutation<PlanResponse, CreatePlanRequest>({
      query: (body) => ({ url: "billing/plans", method: "POST", body }),
      invalidatesTags: ["Plan"],
    }),
    updatePlan: builder.mutation<PlanResponse, { id: string } & UpdatePlanRequest>({
      query: ({ id, ...body }) => ({ url: `billing/plans/${id}`, method: "PUT", body }),
      invalidatesTags: ["Plan"],
    }),
    deletePlan: builder.mutation<void, string>({
      query: (id) => ({ url: `billing/plans/${id}`, method: "DELETE" }),
      invalidatesTags: ["Plan"],
    }),
  }),
});

export const {
  useGetPlansQuery,
  useGetSubscriptionQuery,
  useCreateSubscriptionMutation,
  useCreateCheckoutMutation,
  useFinalizeCheckoutMutation,
  useChangePlanMutation,
  useCancelPlanChangeMutation,
  useGetIssuerPlansQuery,
  useCreatePlanMutation,
  useUpdatePlanMutation,
  useDeletePlanMutation,
} = billingApi;
