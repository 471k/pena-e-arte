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

/** What applies when a studio has no active rule or its rule leaves a field empty (server constants). */
export interface DepositRuleDefaultsResponse {
  cancellationWindowHours:   number;
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
