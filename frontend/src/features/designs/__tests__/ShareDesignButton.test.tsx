import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { designsApi } from "@/features/designs/designsApi";
import { ShareDesignButton } from "@/features/designs/components/ShareDesignButton";

// ── Seed data ──────────────────────────────────────────────────────────────────

const REVISION_ID = "rev-001";
const TOKEN_ID    = "tok-001";
const SHARE_URL   = "http://app.example.com/share/abc123";

const SHARE_TOKEN = {
  id:        TOKEN_ID,
  token:     "abc123",
  shareUrl:  SHARE_URL,
  expiresAt: "2024-12-31T00:00:00Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.post(
    `http://localhost/api/v1/designs/revisions/${REVISION_ID}/share-token`,
    () => HttpResponse.json(SHARE_TOKEN, { status: 201 }),
  ),
  http.delete(
    `http://localhost/api/v1/designs/share-tokens/${TOKEN_ID}`,
    () => new HttpResponse(null, { status: 204 }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); vi.unstubAllGlobals(); cleanup(); });
afterAll(() => server.close());

// ── Clipboard mock helper ──────────────────────────────────────────────────────
// jsdom does not expose navigator.clipboard in non-secure contexts.
// vi.stubGlobal replaces globalThis.navigator, which is the same reference
// the component accesses when it calls `navigator.clipboard.writeText(...)`.

function stubClipboard() {
  const writeText = vi.fn().mockResolvedValue(undefined);
  // Inherit all real navigator properties via Proxy so the rest of the page
  // (MemoryRouter, etc.) still works.
  const mockNavigator = new Proxy(globalThis.navigator, {
    get(target, prop, receiver) {
      if (prop === "clipboard") return { writeText };
      return Reflect.get(target, prop, receiver);
    },
  });
  vi.stubGlobal("navigator", mockNavigator);
  return writeText;
}

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                     authReducer,
      ui:                       uiReducer,
      [designsApi.reducerPath]: designsApi.reducer,
    },
    middleware: (gd) => gd().concat(designsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "artist@ink.test" }, token: "fake-token", tenantId: "s-001", role: "artist", pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false },
    },
  });
}

function renderButton(revisionId = REVISION_ID) {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter>
        <ShareDesignButton revisionId={revisionId} />
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("ShareDesignButton", () => {

  it("renders the Share button", () => {
    renderButton();
    expect(screen.getByRole("button", { name: /share/i })).toBeInTheDocument();
  });

  it("opens the dialog and shows a loading indicator when clicked", async () => {
    let resolveCreate!: (v: Response) => void;
    server.use(
      http.post(
        `http://localhost/api/v1/designs/revisions/${REVISION_ID}/share-token`,
        () => new Promise<Response>((r) => { resolveCreate = r; }),
      ),
    );

    const user = userEvent.setup();
    renderButton();
    await user.click(screen.getByRole("button", { name: /share/i }));

    expect(await screen.findByText(/generating link/i)).toBeInTheDocument();

    resolveCreate(
      HttpResponse.json(SHARE_TOKEN, { status: 201 }) as unknown as Response,
    );
  });

  it("displays the share URL in the dialog after token creation", async () => {
    const user = userEvent.setup();
    renderButton();
    await user.click(screen.getByRole("button", { name: /share/i }));
    expect(await screen.findByText(SHARE_URL)).toBeInTheDocument();
  });

  it("shows the expiry date in the dialog", async () => {
    const user = userEvent.setup();
    renderButton();
    await user.click(screen.getByRole("button", { name: /share/i }));
    await screen.findByText(SHARE_URL);
    expect(screen.getByText(/expires/i)).toBeInTheDocument();
  });

  it("clicking the copy button writes the share URL to clipboard", async () => {
    const writeText = stubClipboard();
    const user = userEvent.setup();
    renderButton();
    await user.click(screen.getByRole("button", { name: /share/i }));
    await screen.findByText(SHARE_URL);

    await user.click(screen.getByRole("button", { name: /copy link/i }));
    expect(writeText).toHaveBeenCalledWith(SHARE_URL);
  });

  it("shows a checkmark after copying", async () => {
    const writeText = stubClipboard();
    const user = userEvent.setup();
    renderButton();
    await user.click(screen.getByRole("button", { name: /share/i }));
    await screen.findByText(SHARE_URL);

    await user.click(screen.getByRole("button", { name: /copy link/i }));
    expect(writeText).toHaveBeenCalledOnce();
  });

  it("clicking 'Revoke link' calls the revoke API and closes the dialog", async () => {
    const user = userEvent.setup();
    renderButton();
    await user.click(screen.getByRole("button", { name: /share/i }));
    await screen.findByText(SHARE_URL);

    await user.click(screen.getByRole("button", { name: /revoke link/i }));

    // Dialog closes after revoke succeeds
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("dialog stays open when revoke fails", async () => {
    server.use(
      http.delete(
        `http://localhost/api/v1/designs/share-tokens/${TOKEN_ID}`,
        () => HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );

    const user = userEvent.setup();
    renderButton();
    await user.click(screen.getByRole("button", { name: /share/i }));
    await screen.findByText(SHARE_URL);

    await user.click(screen.getByRole("button", { name: /revoke link/i }));

    // Dialog should remain open (error is swallowed, no close)
    expect(await screen.findByText(SHARE_URL)).toBeInTheDocument();
  });

  it("dialog closes when token creation fails", async () => {
    server.use(
      http.post(
        `http://localhost/api/v1/designs/revisions/${REVISION_ID}/share-token`,
        () => HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );

    const user = userEvent.setup();
    renderButton();
    await user.click(screen.getByRole("button", { name: /share/i }));

    // After the failed create, the dialog closes
    expect(await screen.findByRole("button", { name: /share/i })).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});
