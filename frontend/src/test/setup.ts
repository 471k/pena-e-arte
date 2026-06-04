import "@testing-library/jest-dom/vitest";
import { afterEach } from "vitest";
import { cleanup } from "@testing-library/react";

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
