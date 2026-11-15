import { useMemo } from "react";
import { useGetMyStudioQuery } from "./studiosApi";
import type { StudioZone } from "@/shared/utils/formatInStudioTimezone";

/**
 * The active studio's IANA timezone as an explicit state (S01). Times are rendered in the studio's zone
 * or not at all: while it loads they read as pending and if it can't be read they say so. They never
 * fall back to the viewer's browser zone, which would show a confident wrong time.
 */
export function useStudioZone(): { zone: StudioZone; retry: () => void } {
  const { data, isError, refetch } = useGetMyStudioQuery();
  const timeZone = data?.timezone;
  const loaded = data !== undefined;
  // Stable identity per state, so callers can list it as a memo dependency.
  const zone = useMemo<StudioZone>(
    () => (timeZone ? { status: "ready", timeZone } : isError || loaded ? { status: "error" } : { status: "loading" }),
    [timeZone, isError, loaded],
  );
  return { zone, retry: () => void refetch() };
}
