export interface SubscriptionResponse {
  id:                   string;
  studioId:             string;
  planId:               string | null;
  pendingPlanId:        string | null;
  status:               "Trialing" | "Active" | "PastDue" | "Cancelled" | "GracePeriod";
  trialExpiresAt:       string | null;
  currentPeriodEnd:     string;
  gracePeriodEnd:       string;
  stripeSubscriptionId: string | null;
}

export interface PlanResponse {
  id:                       string;
  name:                     string;
  billingInterval:          "Monthly" | "Yearly";
  priceMonthly:             number;
  priceYearly:              number;
  yearlyDiscountPercent:    number;
  allowBrandingRemoval:     boolean;
  stripePriceIdMonthly?:    string | null;
  stripePriceIdYearly?:     string | null;
  subscriberCount:          number;
  maxArtists:               number | null;
  maxAppointmentsPerMonth:  number | null;
  maxNotificationsPerMonth: number | null;
  maxStorageGb:             number | null;
  maxLocations:             number | null;
  allowApiAccess:           boolean;
  prioritySupport:          boolean;
  pairedPlanId:             string | null;
}

export interface CreateSubscriptionRequest {
  planId: string;
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
