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
}

export interface GetManualRemindersParams {
  appointmentId?: string;
  clientId?:      string;
}
