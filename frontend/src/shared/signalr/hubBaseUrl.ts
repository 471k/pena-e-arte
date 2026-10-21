/**
 * Where SignalR hubs live, for every hub hook (schedule/design/notification, chat, support, admin
 * notifications, live traffic).
 *
 * In local dev a browser on THIS machine connects straight to the backend (http://localhost:5078):
 * Vite's dev proxy has not reliably forwarded WebSocket upgrades. Any other dev client — a phone, or
 * anyone reaching this machine through a tunnel (test.tattooos.co) or 127.0.0.1 — must use a relative
 * URL through the proxy: "localhost:5078" would point at its own machine. useLiveTrafficHub got this
 * rule on 2026-08-05; the other hooks still hardcoded localhost in dev, so live updates and support
 * chat silently failed for remote testers. Production: same origin, relative.
 */
export function hubBaseUrl(
  hostname: string = window.location.hostname,
  isDev: boolean = import.meta.env.DEV,
): string {
  return isDev && hostname === "localhost" ? "http://localhost:5078" : "";
}
