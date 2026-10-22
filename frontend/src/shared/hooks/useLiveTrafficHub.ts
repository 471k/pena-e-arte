import { useEffect, useState } from "react";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { createHubConnection } from "@/shared/signalr/createHubConnection";
import { hubBaseUrl } from "@/shared/signalr/hubBaseUrl";
import { isTokenExpired } from "@/shared/utils/jwt";
import { platformApi } from "@/features/platform/platformApi";
import type { LiveTrafficSnapshotResponse } from "@/features/platform/platform.types";

export type LiveConnectionState = "connecting" | "connected" | "reconnecting" | "disconnected";

/**
 * TrafficHub has exactly one group ("platform:traffic"), and every connection is added to it
 * automatically in OnConnectedAsync — unlike ScheduleHub/SupportHub, there's no per-client
 * JoinX call to (re)issue after SignalR's automatic reconnect, so the reconnect-group-loss gap
 * documented against useSupportHub in the Decisions Log does not apply to this hub by
 * construction. Left as a comment here so a future reader doesn't "fix" a bug that isn't there.
 */
export function useLiveTrafficHub(enabled: boolean): {
  connectionState: LiveConnectionState;
  lastUpdatedAt: number | null;
} {
  const token    = useAppSelector((s) => s.auth.token);
  const dispatch = useAppDispatch();
  const [connectionState, setConnectionState] = useState<LiveConnectionState>("connecting");
  const [lastUpdatedAt, setLastUpdatedAt] = useState<number | null>(null);

  const active = enabled && !!token;

  useEffect(() => {
    // An expired token (the app reopened after a break) is renewed by the page's first request;
    // the new token re-runs this effect.
    if (!active || isTokenExpired(token!)) return;

    // Direct to the local backend only for a dev browser on this machine; relative otherwise (see hubBaseUrl).
    const hubBase = hubBaseUrl();

    const connection = createHubConnection(`${hubBase}/hubs/traffic`, token!);

    connection.onreconnecting(() => setConnectionState("reconnecting"));
    connection.onreconnected(() => setConnectionState("connected"));
    connection.onclose(() => setConnectionState("disconnected"));

    connection.on("TrafficSnapshotUpdated", (payload: LiveTrafficSnapshotResponse) => {
      dispatch(
        platformApi.util.updateQueryData("getLiveTrafficSnapshot", undefined, () => payload)
      );
      setLastUpdatedAt(Date.now());
    });

    const start = connection.start()
      .then(() => setConnectionState("connected"))
      .catch(() => setConnectionState("disconnected"));

    return () => {
      void start.finally(() => connection.stop());
    };
  }, [active, token, dispatch]);

  return { connectionState: active ? connectionState : "disconnected", lastUpdatedAt };
}
