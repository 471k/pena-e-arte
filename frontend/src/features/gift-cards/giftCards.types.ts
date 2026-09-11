export const GiftCardStatus = {
  Pending:  "Pending",
  Active:   "Active",
  Redeemed: "Redeemed",
  Voided:   "Voided",
} as const;
export type GiftCardStatus = (typeof GiftCardStatus)[keyof typeof GiftCardStatus];

export interface GiftCardResponse {
  id:              string;
  studioId:        string;
  code:            string;
  initialBalance:  number;
  remainingBalance: number;
  purchaserEmail:  string;
  recipientEmail:  string | null;
  status:          GiftCardStatus;
  createdAt:       string;
}

/** Public lookup shape — no purchaser/recipient email. */
export interface GiftCardBalanceResponse {
  remainingBalance: number;
  status:           GiftCardStatus;
}

export interface PurchaseGiftCardRequest {
  studioSlug:     string;
  amount:         number;
  purchaserEmail: string;
  recipientEmail: string | null;
}

export interface PurchaseGiftCardResponse {
  giftCardId:   string;
  clientToken: string | null;
  status:       string;
}

export interface RedeemGiftCardRequest {
  code:          string;
  appointmentId: string;
  amount:        number;
}
