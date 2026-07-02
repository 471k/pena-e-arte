export const AppointmentStatus = {
  Pending:   "Pending",
  Confirmed: "Confirmed",
  Cancelled: "Cancelled",
  Completed: "Completed",
  NoShow:    "NoShow",
} as const;
export type AppointmentStatus = (typeof AppointmentStatus)[keyof typeof AppointmentStatus];

export const DepositStatus = {
  Pending:   "Pending",
  Paid:      "Paid",
  Forfeited: "Forfeited",
  Refunded:  "Refunded",
} as const;
export type DepositStatus = (typeof DepositStatus)[keyof typeof DepositStatus];

export interface AppointmentResponse {
  id:                 string;
  studioId:           string;
  artistId:           string;
  clientId:           string;
  date:               string;
  endDate:            string;
  durationMinutes:    number;
  status:             AppointmentStatus;
  depositStatus:      DepositStatus;
  depositAmount:      number;
  notes:              string | null;
  createdAt:          string;
  cancellationReason?: string | null;
  aftercareSentAt?:    string | null;
  clientName?:         string | null;
}

export interface CreateAppointmentRequest {
  artistId:        string;
  clientId:        string;
  date:            string;
  durationMinutes: number;
  depositRuleId:   string | null;
  notes:           string | null;
}

export interface GetAppointmentsParams {
  from?:     string;
  to?:       string;
  artistId?: string;
}

export interface SlotAvailabilityResponse {
  available: boolean;
  reason:    string | null;
}

export interface CheckSlotAvailabilityParams {
  artistId:        string;
  date:            string;
  durationMinutes: number;
}
