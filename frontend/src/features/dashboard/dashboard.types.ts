import type { AppointmentReadinessSummary, AppointmentStatus } from "@/features/appointments/appointment.types";
import type { PaymentResponse } from "@/features/payments/payment.types";

/** E05a (O01): GET /dashboard/exceptions. Mirrors OwnerExceptionsResponse on the server. */
export const OwnerExceptionKind = {
  Unassigned:           "Unassigned",
  AwaitingConfirmation: "AwaitingConfirmation",
  CashToConfirm:        "CashToConfirm",
  UnpaidDeposit:        "UnpaidDeposit",
  Preparation:          "Preparation",
} as const;
export type OwnerExceptionKind = (typeof OwnerExceptionKind)[keyof typeof OwnerExceptionKind];

/** The deposit due. `payment` is null when the client hasn't started paying: no payment exists yet. */
export interface OwnerExceptionDeposit {
  amount:   number;
  currency: string;
  payment:  PaymentResponse | null;
}

export interface OwnerExceptionItem {
  appointmentId:     string;
  appointmentDate:   string;
  clientName:        string;
  artistName:        string | null;
  appointmentStatus: AppointmentStatus;
  readiness:         AppointmentReadinessSummary | null;
  deposit:           OwnerExceptionDeposit | null;
}

export interface OwnerExceptionGroup {
  kind:  OwnerExceptionKind;
  /** The server's full count; `items` holds at most `maxItemsPerGroup` of them. */
  total: number;
  items: OwnerExceptionItem[];
}

export interface OwnerExceptionsResponse {
  generatedAt:      string;
  windowDays:       number;
  maxItemsPerGroup: number;
  /** Only groups with something in them, ordered by soonest appointment (the server's rule). */
  groups:           OwnerExceptionGroup[];
}
