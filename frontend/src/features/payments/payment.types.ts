export const PaymentStatus = {
  Pending:  "Pending",
  Paid:     "Paid",
  Refunded: "Refunded",
  Failed:   "Failed",
} as const;
export type PaymentStatus = (typeof PaymentStatus)[keyof typeof PaymentStatus];

export interface SessionSplitResponse {
  id:        string;
  paymentId: string;
  label:     string;
  amount:    number;
  paidAt:    string | null;
}

export interface PaymentResponse {
  id:                    string;
  studioId:              string;
  appointmentId:         string;
  clientId:              string;
  amount:                number;
  status:                PaymentStatus;
  stripePaymentIntentId: string | null;
  paidAt:                string | null;
  createdAt:             string;
  sessionSplits:         SessionSplitResponse[];
}

export interface PaymentIntentResponse {
  paymentId:    string;
  clientSecret: string;
  status:       string;
}

export interface CreatePaymentIntentRequest {
  appointmentId: string;
  clientId:      string;
  amount:        number;
  currency:      string;
}

export interface SessionSplitItem {
  label:  string;
  amount: number;
}

export interface UpdateSessionSplitsRequest {
  splits: SessionSplitItem[];
}

export interface GetPaymentsParams {
  lastSeenId?: string;
  pageSize?:   number;
}
