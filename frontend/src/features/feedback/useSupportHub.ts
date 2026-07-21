import { useEffect } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { feedbackApi } from "./feedbackApi";
import type { FeedbackMessageResponse } from "./feedback.types";

// Unlike useSignalR (always-on for the whole layout, joins a studio group), this only
// connects while a SupportTicketThread is mounted, and joins a ticket group instead.
export function useSupportHub(feedbackReportId: string | null) {
  const token         = useAppSelector((s) => s.auth.token);
  const currentUserId = useAppSelector((s) => s.auth.user?.id);
  const dispatch      = useAppDispatch();

  useEffect(() => {
    if (!feedbackReportId || !token) return;

    const hubBase = import.meta.env.DEV ? "http://localhost:5078" : "";
    const conn = new HubConnectionBuilder()
      .withUrl(`${hubBase}/hubs/support`, { accessTokenFactory: () => token! })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    // Block body required — see useSignalR.ts for why an implicit-return arrow breaks
    // SignalR's client (it tries to send dispatch's return value back as an invocation result).
    // Skip the sender's own echoed message — postFeedbackMessage's mutation already
    // invalidates this tag on success, so re-invalidating here would refetch twice per send.
    conn.on("SupportMessageReceived", (message: FeedbackMessageResponse) => {
      if (message.authorUserId === currentUserId) return;
      dispatch(feedbackApi.util.invalidateTags([{ type: "FeedbackMessage", id: feedbackReportId }]));
    });

    // withAutomaticReconnect() assigns a new connection id on reconnect, and SignalR group
    // membership is tied to connection id server-side — without re-joining here, a brief
    // network drop would silently stop future replies from arriving.
    conn.onreconnected(() => {
      conn.invoke("JoinTicket", feedbackReportId).catch(() => {});
    });

    const start = conn.start()
      .then(() => conn.invoke("JoinTicket", feedbackReportId))
      .catch(() => {});

    return () => {
      void start.finally(() => conn.stop());
    };
  }, [feedbackReportId, token, currentUserId, dispatch]);
}
