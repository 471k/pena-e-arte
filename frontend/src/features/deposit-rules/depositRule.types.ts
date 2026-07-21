export interface DepositRuleResponse {
  id:                        string;
  studioId:                  string;
  name:                      string;
  amountFixed:               number | null;
  amountPercent:             number | null;
  isActive:                  boolean;
  createdAt:                 string;
  updatedAt:                 string;
  cancellationWindowHours:   number | null;
  refundPercentOnLateCancel: number;
}

export interface CreateDepositRuleRequest {
  name:                      string;
  amountFixed:               number | null;
  amountPercent:             number | null;
  isActive:                  boolean;
  cancellationWindowHours?:  number | null;
  refundPercentOnLateCancel?: number;
}

export interface UpdateDepositRuleRequest {
  name:                      string;
  amountFixed:               number | null;
  amountPercent:             number | null;
  isActive:                  boolean;
  cancellationWindowHours?:  number | null;
  refundPercentOnLateCancel?: number;
}
