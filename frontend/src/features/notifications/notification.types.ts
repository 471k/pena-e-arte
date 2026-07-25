export interface NotificationLogResponse {
  id:            string;
  recipientId:   string;
  recipientName: string | null;
  channel:       "Email" | "Sms" | "InApp";
  subject:     string | null;
  body:        string;
  sentAt:      string | null;
  isSuccess:   boolean;
  createdAt:   string;
}

export interface NotificationsFilter {
  recipientId?: string;
  channel?:     "Email" | "Sms" | "InApp";
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
