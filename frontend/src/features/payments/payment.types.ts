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
  clientToken:          string | null;
  cashNote:              string | null;
  paidAt:                string | null;
  clientName:            string;
  appointmentDate:       string | null;
  splits?:               SessionSplitResponse[];
  currency:              string;
}

export interface PaymentIntentResponse {
  paymentId:    string;
  clientToken: string;
  status:       string;
}

export interface CreatePaymentIntentRequest {
  appointmentId: string;
  clientId:      string;
  amount:        number;
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

/** Amount/currency let the checkout page render the server's own figures instead of trusting a
 * forgeable `?amount=` query-string param (architecture.md Decisions Log, "Studio currency"). */
export interface ClientTokenResponse {
  clientToken: string;
  amount:      number;
  currency:    string;
}

/** A closed set to switch on for `PaymentCapabilitiesResponse.cardUnavailableReason` — mirrors
 * the backend's own `CardUnavailableReasons` constants exactly; copy stays a frontend concern. */
export const CardUnavailableReasons = {
  ProviderUnsupportedCurrency: "provider_unsupported_currency",
  ProviderNotConnected:        "provider_not_connected",
  ProviderDisabled:            "provider_disabled",
} as const;

export interface PaymentCapabilitiesResponse {
  cardPaymentsAvailable: boolean;
  /** Which POK environment ("staging" | "production") the backend is actually configured
   * against — the single source of truth for the checkout widget's `env` option. Never derive
   * this independently on the client (e.g. from the frontend's own build mode): that can drift
   * out of sync with the backend's real configured host and POK 401s on a mismatch. */
  pokEnvironment: string | null;
  /** Always populated — the studio's currency — so the frontend never has to ask twice. */
  currency: string | null;
  /** One of `CardUnavailableReasons` whenever `cardPaymentsAvailable` is false, so the UI can
   * show the right explanation instead of a generic "unavailable" message. */
  cardUnavailableReason: string | null;
}

export interface ConnectPokAccountRequest {
  keyId:      string;
  keySecret:  string;
  merchantId: string;
}

export interface PokConnectionStatusResponse {
  connected:   boolean;
  merchantId: string | null;
}

export interface PayWithSavedCardRequest {
  appointmentId:      string;
  savedPaymentMethodId: string;
}

export interface PayWithSavedCardDeviceDataCollection {
  url:         string;
  accessToken: string;
}

/** Field names mirror the POK SDK's own `PayerAuthentication` type so the response can be
 * passed straight into `usePOK().payByCardToken(...)` with no remapping. When status is
 * already "Captured"/"Paid" the setup fields are null — nothing more to do. */
export interface PayWithSavedCardSetupResponse {
  paymentId:                string;
  status:                   string;
  orderId:                  string | null;
  cardTokenId:               string | null;
  payerAuthSetupReferenceId: string | null;
  deviceDataCollection:      PayWithSavedCardDeviceDataCollection | null;
}
