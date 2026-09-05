import { useEffect } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { messagingApi } from "./messagingApi";
import type { ChatMessageResponse } from "./messaging.types";

// Unlike useSupportHub (only connects while a SupportTicketThread is mounted, joins a
// ticket group), this mirrors useSignalR's always-on-for-the-whole-layout pattern: ChatHub
// auto-joins a personal `user:{userId}` group on connect (see ChatHub.cs) with no
// join-by-id call needed, so one connection here receives MessageReceived for every
// conversation the user is part of — exactly what the inbox unread badge needs regardless
// of which (if any) thread is currently open.
export function useChatHub() {
  const token = useAppSelector((s) => s.auth.token);
  const currentUserId = useAppSelector((s) => s.auth.user?.id);
  const dispatch = useAppDispatch();

  useEffect(() => {
    if (!token) return;

    const hubBase = import.meta.env.DEV ? "http://localhost:5078" : "";
    const connection = new HubConnectionBuilder()
      .withUrl(`${hubBase}/hubs/chat`, { accessTokenFactory: () => token! })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    // Block bodies required — see useSignalR.ts for why an implicit-return arrow breaks
    // SignalR's client (it tries to send dispatch's return value back as an invocation
    // result).
    connection.on("MessageReceived", (message: ChatMessageResponse) => {
      // Fix mirrored from useSupportHub's documented bug: skip invalidation when this is
      // the echo of the current user's own just-sent message — sendMessage's own
      // invalidatesTags already refetched it once.
      if (message.senderUserId === currentUserId) return;
      dispatch(messagingApi.util.invalidateTags([
        { type: "Messages", id: message.conversationId }, "Conversation", "UnreadCount",
      ]));
    });
    connection.on("ConversationRead", (payload: { id: string; readByUserId: string }) => {
      // Also invalidate the per-conversation Messages tag — without it, the sender's open
      // ConversationThread keeps showing an unread checkmark on their last message until
      // something unrelated forces a refetch, since only the inbox-list Conversation tag
      // was being invalidated here before.
      dispatch(messagingApi.util.invalidateTags([
        { type: "Messages", id: payload.id }, "Conversation",
      ]));
    });

    // ChatHub's group membership is per-connection, auto-joined in OnConnectedAsync — unlike
    // SupportHub there is no JoinTicket to re-invoke after a reconnect, so (unlike
    // useSupportHub) no onreconnected handler is needed: a fresh connection re-runs
    // OnConnectedAsync and rejoins automatically.
    const start = connection.start().catch(() => {});

    return () => {
      void start.finally(() => connection.stop());
    };
  }, [token, currentUserId, dispatch]);
}
