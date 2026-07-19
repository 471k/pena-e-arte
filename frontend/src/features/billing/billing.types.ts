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
  prices:                   PlanPriceResponse[];
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
