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
}

export interface ChatMessageResponse {
  id:             string;
  conversationId: string;
  senderUserId:   string;
  senderRole:     string;
  body:           string;
  createdAt:      string;
  readAt:         string | null;
}

export interface ConversationContactResponse {
  userId:                 string;
  role:                   string;
  displayName:            string;
  avatarUrl:              string | null;
  existingConversationId: string | null;
}
