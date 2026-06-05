export interface DepositRuleResponse {
  id:            string;
  studioId:      string;
  name:          string;
  amountFixed:   number | null;
  amountPercent: number | null;
  isActive:      boolean;
  createdAt:     string;
  updatedAt:     string;
}

export interface CreateDepositRuleRequest {
  name:          string;
  amountFixed:   number | null;
  amountPercent: number | null;
  isActive:      boolean;
}

export interface UpdateDepositRuleRequest {
  name:          string;
  amountFixed:   number | null;
  amountPercent: number | null;
  isActive:      boolean;
}
