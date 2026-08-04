import { useEffect, useRef } from "react";
import { useAppSelector } from "@/app/hooks";
import { router } from "@/app/router";

const VISITOR_ID_KEY = "pea_visitor_id";
const BEACON_URL = "/api/v1/public/traffic/beacon";
const HEARTBEAT_INTERVAL_MS = 20_000;

function getVisitorId(): string {
  let id = localStorage.getItem(VISITOR_ID_KEY);
  if (!id) {
    id = crypto.randomUUID();
    localStorage.setItem(VISITOR_ID_KEY, id);
  }
  return id;
}

// /share/:token embeds a live, still-valid DesignShareToken directly in the path segment —
// this must never reach the backend, where it would sit indefinitely in TrafficEvent.Path.
function redactPath(pathname: string): string {
  if (pathname.startsWith("/share/")) return "/share/[redacted]";
  return pathname;
}

function sendBeacon(path: string, isNavigation: boolean, token: string | null) {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    "X-Visitor-Id": getVisitorId(),
  };
  // Without this, the backend never sees an authenticated caller at all — every signed-in
  // client/artist/owner/issuer would be recorded and shown as a Guest on the live traffic page.
  if (token) headers.Authorization = `Bearer ${token}`;

  void fetch(BEACON_URL, {
    method: "POST",
    headers,
    body: JSON.stringify({ path, isNavigation }),
    keepalive: true,
  }).catch(() => {
    // Fire-and-forget — a failed beacon must never affect the page.
  });
}

/**
 * Mounted once at the app root (see main.tsx). Uses the router's own subscribe() rather than
 * useLocation() — this app's public routes (/discover, /s/:slug, /artist/:slug, ...) are
 * top-level entries with no shared layout wrapper the authenticated routes share via AppRoot,
 * so there is no single component in the tree that every route renders through. subscribe()
 * works regardless of where it's mounted, avoiding a route-tree restructure for this alone.
 */
export function useTrafficBeacon() {
  const lastPathRef = useRef<string | null>(null);

  // "Latest ref" pattern: kept in sync via an effect (never mutated during render) so the
  // location-change/heartbeat handlers below — set up once and never torn down on login/logout
  // — always read the current token without needing to resubscribe from the router or restart
  // the heartbeat interval every time auth state changes.
  const token = useAppSelector((s) => s.auth.token);
  const tokenRef = useRef(token);
  useEffect(() => {
    tokenRef.current = token;
  }, [token]);

  useEffect(() => {
    function handleLocationChange(pathname: string) {
      const path = redactPath(pathname);
      if (path === lastPathRef.current) return;
      lastPathRef.current = path;
      sendBeacon(path, true, tokenRef.current);
    }

    handleLocationChange(router.state.location.pathname);
    const unsubscribe = router.subscribe((state) => {
      handleLocationChange(state.location.pathname);
    });

    let intervalId: ReturnType<typeof setInterval> | null = null;

    function startHeartbeat() {
      if (intervalId) return;
      intervalId = setInterval(() => {
        if (lastPathRef.current) sendBeacon(lastPathRef.current, false, tokenRef.current);
      }, HEARTBEAT_INTERVAL_MS);
    }

    function stopHeartbeat() {
      if (intervalId) {
        clearInterval(intervalId);
        intervalId = null;
      }
    }

    function handleVisibilityChange() {
      if (document.visibilityState === "visible") startHeartbeat();
      else stopHeartbeat();
    }

    if (document.visibilityState === "visible") startHeartbeat();
    document.addEventListener("visibilitychange", handleVisibilityChange);

    return () => {
      unsubscribe();
      stopHeartbeat();
      document.removeEventListener("visibilitychange", handleVisibilityChange);
    };
  }, []);
}

export function TrafficBeacon() {
  useTrafficBeacon();
  return null;
}
