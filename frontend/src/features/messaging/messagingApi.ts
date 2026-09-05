import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  ConversationResponse,
  ChatMessageResponse,
  ConversationContactResponse,
} from "./messaging.types";

export const messagingApi = createApi({
  reducerPath: "messagingApi",
  baseQuery,
  tagTypes: ["Conversation", "Messages", "UnreadCount"],
  endpoints: (builder) => ({
    getConversations: builder.query<ConversationResponse[], void>({
      query: () => "conversations",
      providesTags: ["Conversation"],
    }),
    getContacts: builder.query<ConversationContactResponse[], void>({
      query: () => "conversations/contacts",
    }),
    getUnreadCount: builder.query<number, void>({
      query: () => "conversations/unread-count",
      providesTags: ["UnreadCount"],
    }),
    createConversation: builder.mutation<ConversationResponse, { recipientUserId: string }>({
      query: (body) => ({ url: "conversations", method: "POST", body }),
      invalidatesTags: ["Conversation"],
    }),
    getMessages: builder.query<ChatMessageResponse[], { conversationId: string; before?: string }>({
      query: ({ conversationId, before }) =>
        `conversations/${conversationId}/messages${before ? `?before=${before}` : ""}`,
      providesTags: (_result, _err, arg) => [{ type: "Messages", id: arg.conversationId }],
    }),
    sendMessage: builder.mutation<ChatMessageResponse, { conversationId: string; body: string }>({
      query: ({ conversationId, body }) => ({
        url: `conversations/${conversationId}/messages`,
        method: "POST",
        body: { body },
      }),
      invalidatesTags: (_result, _err, arg) => [
        { type: "Messages", id: arg.conversationId }, "Conversation", "UnreadCount",
      ],
    }),
    markConversationRead: builder.mutation<void, string>({
      query: (conversationId) => ({ url: `conversations/${conversationId}/read`, method: "POST" }),
      invalidatesTags: ["Conversation", "UnreadCount"],
    }),
  }),
});

export const {
  useGetConversationsQuery,
  useGetContactsQuery,
  useGetUnreadCountQuery,
  useCreateConversationMutation,
  useGetMessagesQuery,
  useSendMessageMutation,
  useMarkConversationReadMutation,
} = messagingApi;
