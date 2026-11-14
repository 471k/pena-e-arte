import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";

import uiReducer from "@/features/ui/uiSlice";
import authReducer from "@/features/auth/authSlice";
import { ReadOnlyBanner } from "@/shared/components/ReadOnlyBanner";

function makeStore(readOnlyError: string | null = null, role: string | null = "owner") {
  return configureStore({
    reducer: { auth: authReducer, ui: uiReducer },
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: null, token: null, tenantId: null, role } as any,
      ui:   { readOnlyError, sessionExpired: false, studioSuspended: false, planLimitError: null, impersonationScopeError: null, impersonationSessionExpired: false },
    },
  });
}

function renderBanner(message: string | null = "Studio is in grace period — read-only mode.", role: string | null = "owner") {
  render(
    <Provider store={makeStore(message, role)}>
      <MemoryRouter>
        <ReadOnlyBanner />
      </MemoryRouter>
    </Provider>,
  );
}

describe("ReadOnlyBanner", () => {
  it("renders without crashing when message is set", () => {
    renderBanner();
    expect(screen.getByText(/grace period/i)).toBeInTheDocument();
  });

  it("does not render when message is null", () => {
    renderBanner(null);
    expect(screen.queryByRole("button", { name: /dismiss/i })).not.toBeInTheDocument();
  });

  it("contains text about read-only / grace period", () => {
    renderBanner("Studio is in grace period — read-only mode.");
    expect(screen.getByText(/grace period/i)).toBeInTheDocument();
  });

  it("contains a link to /billing", () => {
    renderBanner();
    const link = screen.getByRole("link", { name: /manage subscription/i });
    expect(link.getAttribute("href")).toContain("/billing");
  });

  it("an admin also gets the server's message and the billing link", () => {
    renderBanner("Studio is in grace period — read-only mode.", "admin");
    expect(screen.getByText(/grace period/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /manage subscription/i })).toBeInTheDocument();
  });

  // /billing is owner/admin only, and the server's message tells the reader to subscribe, which only the
  // owner can do. An artist or a client gets their own sentence and no link to a page that would refuse them.
  it("an artist is told why changes aren't saving and who can fix it, with no billing link", () => {
    renderBanner("Your studio is in the read-only grace period. Subscribe to re-enable write access.", "artist");
    expect(screen.getByText(/subscription needs attention/i)).toBeInTheDocument();
    expect(screen.getByText(/studio owner can sort this out/i)).toBeInTheDocument();
    expect(screen.queryByText(/subscribe to re-enable/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it("a client is told to contact the studio, with no billing link", () => {
    renderBanner("Your studio is in the read-only grace period. Subscribe to re-enable write access.", "client");
    expect(screen.getByText(/contact the studio directly/i)).toBeInTheDocument();
    expect(screen.queryByText(/subscribe to re-enable/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it("dismiss button removes the banner", async () => {
    const user = userEvent.setup();
    renderBanner("Read-only mode.");
    await user.click(screen.getByRole("button", { name: /dismiss/i }));
    expect(screen.queryByText(/read-only mode/i)).not.toBeInTheDocument();
  });
});
