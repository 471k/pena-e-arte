import { HttpTransportType, HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import type { HubConnection } from "@microsoft/signalr";

/**
 * Builds every SignalR connection in the app.
 *
 * Production runs more than one API replica. SignalR's default connect sequence is a "negotiate"
 * request followed by the real connection request, and the second one must reach the SAME server
 * that answered the first — otherwise it fails with a 404 and the client sits "Offline" until a
 * retry happens to land on the right pod (found 2026-09-25: negotiate hit one pod, the WebSocket
 * upgrade hit the other, on every hub). Connecting over WebSockets with `skipNegotiation` removes
 * the first request, so any pod can accept the connection; the Redis backplane on the API then
 * carries messages between pods.
 *
 * Only production builds do this. In dev the Vite proxy doesn't reliably forward WebSocket
 * upgrades (see useSignalR.ts) and there is a single API process, so the default negotiation with
 * its long-polling/SSE fallbacks is kept there.
 */
export function createHubConnection(hubUrl: string, accessToken: string): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(hubUrl, {
      accessTokenFactory: () => accessToken,
      ...(import.meta.env.PROD
        ? { skipNegotiation: true, transport: HttpTransportType.WebSockets }
        : {}),
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();
}
