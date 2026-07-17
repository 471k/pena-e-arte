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
  id:                    string;
  name:                  string;
  billingInterval:       "Monthly" | "Yearly";
  priceMonthly:          number;
  priceYearly:           number;
  yearlyDiscountPercent: number;
  allowBrandingRemoval:  boolean;
  stripePriceIdMonthly?: string | null;
  stripePriceIdYearly?:  string | null;
  subscriberCount:       number;
}

export interface CreateSubscriptionRequest {
  planId: string;
}

export interface BillingPortalResponse {
  url: string;
}
