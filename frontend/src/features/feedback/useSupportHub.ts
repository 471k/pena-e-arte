import { useEffect } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { feedbackApi } from "./feedbackApi";

// Unlike useSignalR (always-on for the whole layout, joins a studio group), this only
// connects while a SupportTicketThread is mounted, and joins a ticket group instead.
export function useSupportHub(feedbackReportId: string | null) {
  const token    = useAppSelector((s) => s.auth.token);
  const dispatch = useAppDispatch();

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
    conn.on("SupportMessageReceived", () => {
      dispatch(feedbackApi.util.invalidateTags([{ type: "FeedbackMessage", id: feedbackReportId }]));
    });

    const start = conn.start()
      .then(() => conn.invoke("JoinTicket", feedbackReportId))
      .catch(() => {});

    return () => {
      void start.finally(() => conn.stop());
    };
  }, [feedbackReportId, token, dispatch]);
}
