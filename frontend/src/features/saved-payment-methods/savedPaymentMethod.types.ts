export interface SavedPaymentMethodResponse {
  id:            string;
  cardBrand:     string | null;
  maskedPan:     string | null;
  expiryMonth:   string | null;
  expiryYear:    string | null;
  isDefault:     boolean;
  createdAt:     string;
  /** Add endpoint only: the card was already on file, so no second copy was added. */
  alreadySaved?: boolean;
}

export interface AddSavedPaymentMethodRequest {
  jwe:                string;
  securityCode:       string | null;
  firstName:          string;
  lastName:           string;
  email:              string;
  countryCode:        string;
  administrativeArea: string | null;
  locality:           string | null;
  address1:           string | null;
  postalCode:         string | null;
  phoneNumber:        string | null;
}
