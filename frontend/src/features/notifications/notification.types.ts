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

export type NotificationType =
  | "AppointmentCreated"
  | "AppointmentConfirmed"
  | "AppointmentCancelled"
  | "DepositCaptured"
  | "PaymentRefunded"
  | "IntakeFormSubmitted"
  | "ConsentFormSigned"
  | "DesignReviewed";

export type NotificationChannel = "Email" | "Sms";

export interface NotificationPreferenceItem {
  type:      NotificationType;
  channel:   NotificationChannel;
  isEnabled: boolean;
}

export interface NotificationPreferencesResponse {
  preferences: NotificationPreferenceItem[];
}
