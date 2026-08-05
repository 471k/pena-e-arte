import { useEffect, useState } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
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
    if (!active) return;

    // Connecting directly to the backend (bypassing Vite's dev proxy) is deliberate for real
    // local dev — see useSignalR.ts's note that the proxy doesn't reliably forward WebSocket
    // upgrades. But "localhost:5078" only means anything when the browser and the dev server
    // are the same machine; a remote client (e.g. testing through a Cloudflare Tunnel to a
    // public hostname) needs a relative URL instead, routed through the proxy on this machine.
    const isLocalDevBrowser = import.meta.env.DEV && window.location.hostname === "localhost";
    const hubBase = isLocalDevBrowser ? "http://localhost:5078" : "";

    const connection = new HubConnectionBuilder()
      .withUrl(`${hubBase}/hubs/traffic`, { accessTokenFactory: () => token! })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

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
