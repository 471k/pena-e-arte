export interface PromoCodeResponse {
  id:               string;
  studioId:         string;
  code:             string;
  amountFixed:      number | null;
  amountPercent:    number | null;
  isActive:         boolean;
  expiresAt:        string | null;
  maxRedemptions:   number | null;
  redemptionCount:  number;
  createdAt:        string;
  updatedAt:        string;
}

export interface CreatePromoCodeRequest {
  code:            string;
  amountFixed:     number | null;
  amountPercent:   number | null;
  isActive:        boolean;
  expiresAt?:      string | null;
  maxRedemptions?: number | null;
}

export interface UpdatePromoCodeRequest {
  code:            string;
  amountFixed:     number | null;
  amountPercent:   number | null;
  isActive:        boolean;
  expiresAt?:      string | null;
  maxRedemptions?: number | null;
}
