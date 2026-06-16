export interface NotificationLogResponse {
  id:            string;
  recipientId:   string;
  recipientName: string | null;
  channel:       "Email" | "Sms";
  subject:     string | null;
  body:        string;
  sentAt:      string | null;
  isSuccess:   boolean;
  createdAt:   string;
}

export interface NotificationsFilter {
  recipientId?: string;
  channel?:     "Email" | "Sms";
  from?:        string;
  to?:          string;
}
