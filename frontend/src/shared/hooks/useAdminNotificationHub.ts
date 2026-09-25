import { useEffect } from "react";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { createHubConnection } from "@/shared/signalr/createHubConnection";
import { notificationsApi } from "@/features/notifications/notificationsApi";
import { incrementUnread } from "@/features/notifications/notificationsSlice";

// Deliberately separate from useSignalR.ts (parameterized by studioId — an admin
// connection has no single tenant studio to key off for this purpose). Connects only
// /hubs/notification and does not invoke JoinStudio: the server-side conditional
// auto-join in NotificationHub.OnConnectedAsync handles group membership for admins.
export function useAdminNotificationHub() {
  const token    = useAppSelector((s) => s.auth.token);
  const dispatch = useAppDispatch();

  useEffect(() => {
    if (!token) return;

    // In dev the Vite proxy doesn't reliably forward WebSocket upgrades for SignalR,
    // so connect directly to the backend. In production the hubs are on the same origin.
    const hubBase = import.meta.env.DEV ? "http://localhost:5078" : "";

    const connection = createHubConnection(`${hubBase}/hubs/notification`, token!);

    // See useSignalR.ts: a single-expression arrow handler implicitly returns
    // dispatch's return value, which SignalR tries to send back as an invocation
    // result the server never asked for — block bodies only.
    connection.on("NotificationReceived", () => {
      dispatch(notificationsApi.util.invalidateTags(["NotificationLog"]));
      dispatch(incrementUnread());
    });

    const start = connection.start().catch(() => {});

    return () => {
      void start.finally(() => connection.stop());
    };
  }, [token, dispatch]);
}
