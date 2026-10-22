import { useEffect } from "react";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { createHubConnection } from "@/shared/signalr/createHubConnection";
import { hubBaseUrl } from "@/shared/signalr/hubBaseUrl";
import { notificationsApi } from "@/features/notifications/notificationsApi";
import { incrementUnreadIfVisible } from "@/features/notifications/incrementUnreadIfVisible";
import type { NotificationLogResponse } from "@/features/notifications/notification.types";
import { isTokenExpired } from "@/shared/utils/jwt";

// Deliberately separate from useSignalR.ts (parameterized by studioId — an admin
// connection has no single tenant studio to key off for this purpose). Connects only
// /hubs/notification and does not invoke JoinStudio: the server-side conditional
// auto-join in NotificationHub.OnConnectedAsync handles group membership for admins.
export function useAdminNotificationHub() {
  const token    = useAppSelector((s) => s.auth.token);
  const dispatch = useAppDispatch();

  useEffect(() => {
    if (!token || isTokenExpired(token)) return;

    // Direct to the local backend only for a dev browser on this machine; relative otherwise (see hubBaseUrl).
    const hubBase = hubBaseUrl();

    const connection = createHubConnection(`${hubBase}/hubs/notification`, token!);

    // See useSignalR.ts: a single-expression arrow handler implicitly returns
    // dispatch's return value, which SignalR tries to send back as an invocation
    // result the server never asked for — block bodies only.
    connection.on("NotificationReceived", (pushed?: NotificationLogResponse) => {
      dispatch(notificationsApi.util.invalidateTags(["NotificationLog"]));
      void incrementUnreadIfVisible(dispatch, pushed);
    });

    const start = connection.start().catch(() => {});

    return () => {
      void start.finally(() => connection.stop());
    };
  }, [token, dispatch]);
}
