import "@testing-library/jest-dom/vitest";
import { afterEach, vi } from "vitest";
import { configure } from "@testing-library/react";

// Default findBy*/waitFor timeout (1000ms) assumes near-instant async resolution.
// Under the full suite's parallel worker load, CPU contention can genuinely push a
// correct async validation/re-render (e.g. react-hook-form + zodResolver) past that
// default even though nothing is actually broken — the same interaction is instant
// when the file runs in isolation. Raising this globally fixes that whole class of
// full-suite-only flakiness rather than bumping timeouts test-by-test as each one
// is hit by it.
configure({ asyncUtilTimeout: 3000 });

// Mock SignalR globally — prevents real WebSocket connections in JSDOM.
// HubConnectionBuilder.build() throws "Cannot resolve '/hubs/schedule'" without this.
vi.mock("@microsoft/signalr", () => {
  const noop = () => {};
  const connection = {
    on:            noop,
    onreconnected: noop,
    start:         () => Promise.resolve(),
    stop:          () => Promise.resolve(),
    invoke:        () => Promise.resolve(),
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

// Unlike real browsers, JSDOM re-dispatches focus/focusin events even when an
// element is already the activeElement. Radix's FocusScope/DismissableLayer
// re-focus the last-active element as part of its own focus event handling,
// which JSDOM then re-fires — an infinite loop that manifests as "Maximum call
// stack size exceeded" whenever a Dialog's content changes shape while open
// (e.g. a loading state resolving mid-render). Match real browser behavior by
// making focus() a no-op when the element is already focused.
const originalFocus = window.HTMLElement.prototype.focus;
window.HTMLElement.prototype.focus = function (this: HTMLElement, ...args) {
  if (document.activeElement !== this) {
    originalFocus.apply(this, args);
  }
};

// Radix UI's floating positioning uses ResizeObserver.
class MockResizeObserver {
  observe()    {}
  unobserve()  {}
  disconnect() {}
}
window.ResizeObserver = MockResizeObserver as unknown as typeof ResizeObserver;

// JSDOM has no layout engine and doesn't implement matchMedia at all. Default
// min-width queries to matching — JSDOM's own default viewport (1024×768) is
// desktop-sized, and no test currently depends on a mobile default. Tests that
// need to simulate a narrow viewport can reassign window.matchMedia themselves.
window.matchMedia = window.matchMedia || ((query: string) => ({
  matches: query.includes("min-width"),
  media: query,
  onchange: null,
  addListener: () => {},
  removeListener: () => {},
  addEventListener: () => {},
  removeEventListener: () => {},
  dispatchEvent: () => false,
})) as typeof window.matchMedia;

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
