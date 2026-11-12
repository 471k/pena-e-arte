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
  /** Given back: refunded after a charge, or released from a hold in a partial capture. */
  refundedAmount?:       number | null;
  /** The appointment's status when the API loaded it ("Cancelled" = late-cancel outcome). */
  appointmentStatus?:    string | null;
  /** Lek reference frozen when the client paid (Albanian EUR studios only; null otherwise).
   * A refund's lek is refundedAmount x referenceRate, never a later rate. */
  referenceAmount?:      number | null;
  referenceCurrency?:    string | null;
  referenceRate?:        number | null;
  /** "yyyy-MM-dd" — the date the Bank of Albania states for referenceRate. */
  referenceRateDate?:    string | null;
}

export interface PaymentIntentResponse {
  paymentId:    string;
  clientToken: string;
  status:       string;
  /** What the card will be charged (client deposit flow); absent on other intents. */
  amount?:      number | null;
  currency?:    string | null;
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

/** GET /payments (E05b-1): newest first, every filter applied on the server. */
export interface GetPaymentsParams {
  lastSeenId?: string;
  pageSize?:   number;
  /** Client or artist name, any case. */
  search?:     string;
  /** One PaymentStatus by its exact name. */
  status?:     PaymentStatus;
  /** Studio-local dates (yyyy-MM-dd) on when the payment was paid; unpaid payments drop out when set. */
  from?:       string;
  to?:         string;
}

/** Amount/currency let the checkout page render the server's own figures instead of trusting a
 * forgeable `?amount=` query-string param (architecture.md Decisions Log, "Studio currency"). */
export interface ClientTokenResponse {
  clientToken: string;
  amount:      number;
  currency:    string;
  /** The lek frozen on this payment when its order was created (Albanian EUR studios; null otherwise). */
  referenceAmount?:   number | null;
  referenceCurrency?: string | null;
  /** The payment's studio country (with currency: lek + the "charged in euros" line for AL + EUR). */
  studioCountryCode?: string | null;
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
  /** True only when `cardPaymentsAvailable` is true and the platform's saved-card switch
   * (`Payments:SavedCardsEnabled`, backlog C-9) is on. Gates the "Saved card" tab, the
   * Payment Methods page's add-card form, its nav entry and its tour step. */
  savedCardsAvailable: boolean;
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

// ── Unsettled card payments (E05b-2, O06) ─────────────────────────────────────
// Camelcase mirror of Pena_e_Arte.Contracts.Responses.UnsettledPaymentsResponse: what the reconciliation
// job hasn't settled, exactly as it last recorded it. Amounts are per payment and never totalled.

export const UnsettledPaymentKind = {
  ProviderDisagrees:   "ProviderDisagrees",
  OnHold:              "OnHold",
  AwaitingCardPayment: "AwaitingCardPayment",
} as const;
export type UnsettledPaymentKind = (typeof UnsettledPaymentKind)[keyof typeof UnsettledPaymentKind];

export type ProviderStatusAtCheck = "Pending" | "Authorized" | "Captured" | "Canceled" | "Refunded" | "Failed";

export interface UnsettledPaymentItem {
  paymentId:             string;
  appointmentId:         string;
  appointmentDate:       string;
  appointmentStatus:     string;
  clientName:            string;
  amount:                number;
  currency:              string;
  /** Local status: "Pending" (not paid) or "Captured" (held). */
  status:                "Pending" | "Captured";
  createdAt:             string;
  /** AwaitingCardPayment only: after this the unpaid order is cancelled automatically. */
  releaseDeadline:       string | null;
  /** When the provider last answered about it; null = not asked yet. */
  providerCheckedAt:     string | null;
  /** What it answered; null with a check time = it had no record of the order. */
  providerStatusAtCheck: ProviderStatusAtCheck | null;
  checkOverdue:          boolean;
}

export interface UnsettledPaymentGroup {
  kind:  UnsettledPaymentKind;
  total: number;
  items: UnsettledPaymentItem[];
}

export interface UnsettledPaymentsResponse {
  generatedAt:            string;
  checkOverdueAfterHours: number;
  maxItemsPerGroup:       number;
  groups:                 UnsettledPaymentGroup[];
}
