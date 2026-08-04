import { useEffect } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { platformApi } from "@/features/platform/platformApi";
import type { LiveTrafficSnapshotResponse } from "@/features/platform/platform.types";

/**
 * TrafficHub has exactly one group ("platform:traffic"), and every connection is added to it
 * automatically in OnConnectedAsync — unlike ScheduleHub/SupportHub, there's no per-client
 * JoinX call to (re)issue after SignalR's automatic reconnect, so the reconnect-group-loss gap
 * documented against useSupportHub in the Decisions Log does not apply to this hub by
 * construction. Left as a comment here so a future reader doesn't "fix" a bug that isn't there.
 */
export function useLiveTrafficHub(enabled: boolean) {
  const token    = useAppSelector((s) => s.auth.token);
  const dispatch = useAppDispatch();

  useEffect(() => {
    if (!enabled || !token) return;

    const hubBase = import.meta.env.DEV ? "http://localhost:5078" : "";

    const connection = new HubConnectionBuilder()
      .withUrl(`${hubBase}/hubs/traffic`, { accessTokenFactory: () => token! })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on("TrafficSnapshotUpdated", (payload: LiveTrafficSnapshotResponse) => {
      dispatch(
        platformApi.util.updateQueryData("getLiveTrafficSnapshot", undefined, () => payload)
      );
    });

    const start = connection.start().catch(() => {});

    return () => {
      void start.finally(() => connection.stop());
    };
  }, [enabled, token, dispatch]);
}
