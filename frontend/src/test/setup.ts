import "@testing-library/jest-dom/vitest";
import { afterEach, vi } from "vitest";

// Mock SignalR globally — prevents real WebSocket connections in JSDOM.
// HubConnectionBuilder.build() throws "Cannot resolve '/hubs/schedule'" without this.
vi.mock("@microsoft/signalr", () => {
  const noop = () => {};
  const connection = {
    on:     noop,
    start:  () => Promise.resolve(),
    stop:   () => Promise.resolve(),
    invoke: () => Promise.resolve(),
  };
  const builder = {
    withUrl:               () => builder,
    withAutomaticReconnect:() => builder,
    configureLogging:      () => builder,
    build:                 () => connection,
  };
  return {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    HubConnectionBuilder: vi.fn(function(this: any) { return builder; }),
    LogLevel:             { Warning: 1, Information: 2, Error: 3, None: 6 },
  };
});
import { cleanup } from "@testing-library/react";

// Radix UI uses DOM APIs that JSDOM does not implement.
// Polyfill them so Radix Select / Popover / Dialog / etc. work in tests.
window.HTMLElement.prototype.hasPointerCapture    = () => false;
window.HTMLElement.prototype.setPointerCapture    = () => {};
window.HTMLElement.prototype.releasePointerCapture = () => {};
window.HTMLElement.prototype.scrollIntoView       = () => {};

// Radix UI's floating positioning uses ResizeObserver.
class MockResizeObserver {
  observe()    {}
  unobserve()  {}
  disconnect() {}
}
window.ResizeObserver = MockResizeObserver as unknown as typeof ResizeObserver;

// globals: false means @testing-library/react never auto-registers afterEach(cleanup).
// Each test would otherwise accumulate the previous test's DOM.
afterEach(() => {
  cleanup();
});

// RTK Query calls `new Request(url, config)` directly before calling fetchFn.
// Node's undici Request rejects relative URLs. Subclass it so that relative
// paths are resolved against http://localhost, matching MSW handler patterns.
const OriginalRequest = globalThis.Request;
class LocalhostRequest extends OriginalRequest {
  constructor(input: RequestInfo | URL, init?: RequestInit) {
    if (typeof input === "string" && input.startsWith("/")) {
      super(`http://localhost${input}`, init);
    } else {
      super(input as string, init);
    }
  }
}
(globalThis as { Request: typeof OriginalRequest }).Request = LocalhostRequest;
