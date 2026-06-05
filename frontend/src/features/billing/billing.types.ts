export interface SubscriptionResponse {
  id:                   string;
  studioId:             string;
  planId:               string | null;
  status:               "Trialing" | "Active" | "PastDue" | "Cancelled" | "GracePeriod";
  trialExpiresAt:       string;
  currentPeriodEnd:     string;
  gracePeriodEnd:       string;
  stripeSubscriptionId: string | null;
  isStripeConnected:    boolean;
}

export interface PlanResponse {
  id:                   string;
  name:                 string;
  billingInterval:      "Monthly" | "Yearly";
  priceMonthly:         number;
  priceYearly:          number;
  yearlyDiscountPercent: number;
}

export interface CreateSubscriptionRequest {
  planId: string;
}
