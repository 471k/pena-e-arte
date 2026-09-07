import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type { SubscriptionResponse, PlanResponse, CreateSubscriptionRequest, BillingPortalResponse, PlanUsageResponse } from "./billing.types";

export interface PlanPriceRequest {
  interval:       string;
  price:          number;
  stripePriceId?: string | null;
  isActive?:      boolean;
}

export interface CreatePlanRequest {
  name:                     string;
  yearlyDiscountPercent:    number;
  prices:                   PlanPriceRequest[];
  maxArtists?:              number | null;
  maxAppointmentsPerMonth?: number | null;
  maxNotificationsPerMonth?: number | null;
  maxStorageGb?:            number | null;
  maxLocations?:            number | null;
  allowApiAccess?:          boolean;
  prioritySupport?:         boolean;
  allowBrandingRemoval?:    boolean;
}

export interface UpdatePlanRequest {
  name:                     string;
  yearlyDiscountPercent:    number;
  prices:                   PlanPriceRequest[];
  allowBrandingRemoval:     boolean;
  maxArtists?:              number | null;
  maxAppointmentsPerMonth?: number | null;
  maxNotificationsPerMonth?: number | null;
  maxStorageGb?:            number | null;
  maxLocations?:            number | null;
  allowApiAccess?:          boolean;
  prioritySupport?:         boolean;
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
    getPlanUsage: builder.query<PlanUsageResponse | null, void>({
      query: () => "billing/usage",
      providesTags: ["Subscription"],
    }),
    createSubscription: builder.mutation<SubscriptionResponse, CreateSubscriptionRequest>({
      query: (body) => ({ url: "billing/subscription", method: "POST", body }),
      invalidatesTags: ["Subscription"],
    }),
    // Card subscribe via Stripe-hosted Checkout — returns a URL to redirect to.
    createCheckout: builder.mutation<
      { url: string },
      { planId: string; billingInterval: string; successUrl: string; cancelUrl: string }
    >({
      query: (body) => ({ url: "billing/subscription/checkout", method: "POST", body }),
    }),
    // Reconcile after returning from Checkout (in case the webhook was missed).
    finalizeCheckout: builder.mutation<SubscriptionResponse | null, { sessionId: string }>({
      query: (body) => ({ url: "billing/subscription/checkout/finalize", method: "POST", body }),
      invalidatesTags: ["Subscription"],
    }),
    // Plan switching: upgrades apply immediately (prorated), downgrades at period end
    changePlan: builder.mutation<SubscriptionResponse, { planId: string; billingInterval: string }>({
      query: (body) => ({ url: "billing/subscription/plan", method: "PUT", body }),
      invalidatesTags: ["Subscription"],
    }),
    cancelPlanChange: builder.mutation<SubscriptionResponse, void>({
      query: () => ({ url: "billing/subscription/plan/pending", method: "DELETE" }),
      invalidatesTags: ["Subscription"],
    }),
    // Opens a Stripe Customer Portal session for the owner to manage payment method,
    // download invoices, and cancel. Returns a Stripe-hosted URL to redirect to.
    createPortalSession: builder.mutation<BillingPortalResponse, { returnUrl: string }>({
      query: (body) => ({ url: "billing/portal", method: "POST", body }),
    }),
    // Admin plan management
    getAdminPlans: builder.query<PlanResponse[], void>({
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
  useGetPlanUsageQuery,
  useCreateSubscriptionMutation,
  useCreateCheckoutMutation,
  useFinalizeCheckoutMutation,
  useChangePlanMutation,
  useCancelPlanChangeMutation,
  useCreatePortalSessionMutation,
  useGetAdminPlansQuery,
  useCreatePlanMutation,
  useUpdatePlanMutation,
  useDeletePlanMutation,
} = billingApi;
