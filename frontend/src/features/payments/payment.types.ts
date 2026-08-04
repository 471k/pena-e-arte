export const PaymentStatus = {
  Pending:     "Pending",
  CashPending: "CashPending",
  Captured:    "Captured",
  Paid:        "Paid",
  Refunded:    "Refunded",
  Failed:      "Failed",
} as const;
export type PaymentStatus = (typeof PaymentStatus)[keyof typeof PaymentStatus];

export const PaymentMethod = {
  Card: "Card",
  Cash: "Cash",
} as const;
export type PaymentMethod = (typeof PaymentMethod)[keyof typeof PaymentMethod];

export interface PaymentResponse {
  id:                    string;
  appointmentId:         string;
  amount:                number;
  status:                PaymentStatus;
  method:                PaymentMethod;
  providerReferenceId: string | null;
  clientSecret:          string | null;
  cashNote:              string | null;
  paidAt:                string | null;
  clientName:            string;
  appointmentDate:       string | null;
  splits?:               SessionSplitResponse[];
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

export interface SessionSplitResponse {
  id:        string;
  paymentId: string;
  label:     string;
  amount:    number;
  paidAt:    string | null;
}

export interface UpdateSessionSplitsRequest {
  splits: SessionSplitItem[];
}

export interface GetPaymentsParams {
  lastSeenId?: string;
  pageSize?:   number;
}

export interface ClientSecretResponse {
  clientSecret: string;
}
