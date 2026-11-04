export interface ConversationResponse {
  id:                 string;
  otherUserId:        string;
  otherRole:          string;
  otherDisplayName:   string;
  otherAvatarUrl:     string | null;
  lastMessageAt:      string | null;
  lastMessagePreview: string | null;
  lastMessageFromMe:  boolean;
  unreadCount:        number;
  createdAt:          string;
  /** The appointment the last message is about, if any (the id is stored; the label is resolved
   * from the appointment on every read, so a reschedule shows straight away). */
  lastMessageAppointmentId?: string | null;
  lastMessageAppointment?:   MessageAppointmentLabel | null;
}

/** "Which appointment is this message about" — resolved server-side on every read. `startsAt` is
 * UTC: render it in the studio's timezone. `serviceName` is null when there is no service. */
export interface MessageAppointmentLabel {
  appointmentId: string;
  startsAt:      string;
  serviceName:   string | null;
}

export interface ChatMessageResponse {
  id:             string;
  conversationId: string;
  senderUserId:   string;
  senderRole:     string;
  body:           string;
  createdAt:      string;
  readAt:         string | null;
  /** The appointment the message is about, when sent from the client appointment page. */
  appointmentId?: string | null;
  /** Its label — present on GET messages; the send response and realtime push carry the id only. */
  appointment?:   MessageAppointmentLabel | null;
}

export interface ConversationContactResponse {
  userId:                 string;
  role:                   string;
  displayName:            string;
  avatarUrl:              string | null;
  existingConversationId: string | null;
}
