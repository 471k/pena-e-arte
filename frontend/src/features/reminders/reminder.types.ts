export interface CreateManualReminderRequest {
  appointmentId?: string | null;
  clientId?:      string | null;
  artistId?:      string | null;
  recipientName?: string | null;
  recipientPhone?: string | null;
  message?:       string | null;
  scheduledFor?:  string | null;
}

export type ManualReminderStatus = "Scheduled" | "Sent" | "Failed" | "Cancelled";

/** Why a reminder is Failed — a category only, never provider text. Null/absent for other statuses. */
export type ManualReminderFailureReason = "SmsOptOut" | "PlanLimit" | "UnknownOutcome" | "ProviderError";

export interface ManualReminderResponse {
  id:            string;
  appointmentId: string | null;
  clientId:      string | null;
  recipientName: string;
  recipientPhone: string;
  message:       string | null;
  scheduledFor:  string;
  status:        ManualReminderStatus;
  sentAt:        string | null;
  createdAt:     string;
  failureReason?: ManualReminderFailureReason | null;
}

export const FAILURE_REASON_LABELS: Record<ManualReminderFailureReason, string> = {
  SmsOptOut:      "Client has opted out of SMS",
  PlanLimit:      "Monthly notification limit reached",
  UnknownOutcome: "Delivery couldn't be confirmed — not re-sent to avoid texting twice",
  ProviderError:  "The SMS provider couldn't deliver it",
};

export interface GetManualRemindersParams {
  appointmentId?: string;
  clientId?:      string;
}
