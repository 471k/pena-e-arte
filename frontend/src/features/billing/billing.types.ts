export interface PlanPriceResponse {
  id:            string;
  interval:      "Monthly" | "Yearly";
  price:         number;
  stripePriceId: string | null;
  isActive:      boolean;
}

export interface SubscriptionResponse {
  id:                     string;
  studioId:               string;
  planId:                 string | null;
  billingInterval:        "Monthly" | "Yearly";
  pendingPlanId:          string | null;
  pendingBillingInterval: "Monthly" | "Yearly" | null;
  status:                 "Trialing" | "Active" | "PastDue" | "Cancelled" | "GracePeriod";
  trialExpiresAt:         string | null;
  currentPeriodEnd:       string;
  gracePeriodEnd:         string;
  stripeSubscriptionId:   string | null;
  cancelAtPeriodEnd:      boolean;
}

export interface PlanResponse {
  id:                       string;
  name:                     string;
  yearlyDiscountPercent:    number;
  allowBrandingRemoval:     boolean;
  subscriberCount:          number;
  maxArtists:               number | null;
  maxAppointmentsPerMonth:  number | null;
  maxNotificationsPerMonth: number | null;
  maxStorageGb:             number | null;
  maxLocations:             number | null;
  allowApiAccess:           boolean;
  prioritySupport:          boolean;
  allowMarketingCampaigns:  boolean;
  prices:                   PlanPriceResponse[];
  // Computed server-side from the active Monthly/Yearly prices (D7) — null when either
  // is missing/inactive, Monthly is 0, or the computed saving isn't positive. Never read
  // yearlyDiscountPercent for owner-facing copy; it's admin-input-only.
  yearlySavingAmount:       number | null;
  yearlyMonthsFree:         number | null;
}

export interface CreateSubscriptionRequest {
  planId:          string;
  billingInterval: "Monthly" | "Yearly";
}

export interface PlanUsageDimension {
  current: number;
  max:     number | null;
}

export interface PlanUsageResponse {
  planName:              string;
  artists:               PlanUsageDimension;
  appointmentsPerMonth:  PlanUsageDimension;
  notificationsPerMonth: PlanUsageDimension;
  storageGb:             PlanUsageDimension;
  locations:             PlanUsageDimension;
}

export interface BillingPortalResponse {
  url: string;
}

// Returns the active price for the given plan at the given billing interval, or
// undefined when the tier doesn't offer that interval (or it's currently disabled).
export function priceFor(plan: PlanResponse, interval: "Monthly" | "Yearly"): PlanPriceResponse | undefined {
  return plan.prices.find((p) => p.interval === interval && p.isActive);
}

// Same as priceFor, but a paid price with no linked Stripe price isn't purchasable yet
// (checkout would fail with "This plan is not available for online checkout"). Free
// (price === 0) is always purchasable once active. Used by the subscribe flow only —
// BillingPage keeps priceFor so it can still display a current plan even if unlinked.
export function purchasablePriceFor(plan: PlanResponse, interval: "Monthly" | "Yearly"): PlanPriceResponse | undefined {
  const price = priceFor(plan, interval);
  if (!price) return undefined;
  return price.price === 0 || price.stripePriceId !== null ? price : undefined;
}
