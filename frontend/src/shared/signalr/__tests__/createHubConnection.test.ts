import { afterEach, describe, expect, it, vi } from "vitest";

// Replaces the global SignalR mock from test/setup.ts with one whose withUrl is a spy, so the
// test can read back exactly which options the factory passes.
const { withUrl, withAutomaticReconnect, configureLogging, build } = vi.hoisted(() => ({
  withUrl: vi.fn(),
  withAutomaticReconnect: vi.fn(),
  configureLogging: vi.fn(),
  build: vi.fn(() => ({ fake: "connection" })),
}));

vi.mock("@microsoft/signalr", () => {
  const builder = {
    withUrl: (...args: unknown[]) => { withUrl(...args); return builder; },
    withAutomaticReconnect: () => { withAutomaticReconnect(); return builder; },
    configureLogging: (...args: unknown[]) => { configureLogging(...args); return builder; },
    build,
  };
  return {
    HubConnectionBuilder: vi.fn(function () { return builder; }),
    LogLevel: { Warning: 1 },
    HttpTransportType: { None: 0, WebSockets: 1, ServerSentEvents: 2, LongPolling: 4 },
  };
});

import { createHubConnection } from "../createHubConnection";

interface HubOptions {
  accessTokenFactory: () => string;
  skipNegotiation?: boolean;
  transport?: number;
}

function optionsPassedToWithUrl(): HubOptions {
  return withUrl.mock.calls[0][1] as HubOptions;
}

describe("createHubConnection", () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.clearAllMocks();
  });

  it("production: connects over WebSockets only and skips negotiate, so any API pod can accept it", () => {
    vi.stubEnv("PROD", true);

    createHubConnection("/hubs/traffic", "tok");

    const options = optionsPassedToWithUrl();
    expect(options.skipNegotiation).toBe(true);
    expect(options.transport).toBe(1); // HttpTransportType.WebSockets
  });

  it("dev: keeps default negotiation with its fallback transports", () => {
    vi.stubEnv("PROD", false);

    createHubConnection("http://localhost:5078/hubs/traffic", "tok");

    const options = optionsPassedToWithUrl();
    expect(options.skipNegotiation).toBeUndefined();
    expect(options.transport).toBeUndefined();
  });

  it("passes the URL and supplies the access token to the hub", () => {
    createHubConnection("/hubs/chat", "my-token");

    expect(withUrl.mock.calls[0][0]).toBe("/hubs/chat");
    expect(optionsPassedToWithUrl().accessTokenFactory()).toBe("my-token");
  });

  it("enables automatic reconnect and warning-level logging, then builds", () => {
    createHubConnection("/hubs/chat", "tok");

    expect(withAutomaticReconnect).toHaveBeenCalledTimes(1);
    expect(configureLogging).toHaveBeenCalledWith(1);
    expect(build).toHaveBeenCalledTimes(1);
  });
});
