import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { Toaster } from "sonner";

import authReducer from "@/features/auth/authSlice";
import { clientsApi } from "@/features/clients/clientsApi";
import { EraseClientDataSection } from "@/features/clients/components/EraseClientDataSection";

const CLIENT_ID = "client-001";

let cancelCalled = false;
const server = setupServer(
  http.post(`http://localhost/api/v1/clients/${CLIENT_ID}/erase-data`, () =>
    new HttpResponse(null, { status: 204 })),
  http.post(`http://localhost/api/v1/clients/${CLIENT_ID}/cancel-erasure`, () => {
    cancelCalled = true;
    return new HttpResponse(null, { status: 204 });
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); cancelCalled = false; });
afterAll(() => server.close());

function renderSection(erasureRequestedAt: string | null) {
  const store = configureStore({
    reducer: { auth: authReducer, [clientsApi.reducerPath]: clientsApi.reducer },
    middleware: (gd) => gd().concat(clientsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "owner@test.com" }, token: "t", tenantId: "s1", role: "owner", pendingReferralCode: null, impersonation: null } as any,
    },
  });
  render(
    <Provider store={store}>
      <Toaster />
      <MemoryRouter>
        <EraseClientDataSection
          clientId={CLIENT_ID}
          clientName="Ana Ferreira"
          erasureRequestedAt={erasureRequestedAt}
        />
      </MemoryRouter>
    </Provider>,
  );
}

describe("EraseClientDataSection", () => {
  it("shows a pending-erasure banner with a 'Cancel erasure request' button when erasure is pending", () => {
    renderSection("2026-09-01T00:00:00.000Z");
    expect(screen.getByText(/data erasure requested/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /cancel erasure request/i })).toBeInTheDocument();
  });

  it("does not show the cancel button when there is no pending erasure", () => {
    renderSection(null);
    expect(screen.queryByRole("button", { name: /cancel erasure request/i })).not.toBeInTheDocument();
  });

  it("confirming the cancel dialog calls the cancel-erasure endpoint and shows a success toast", async () => {
    const user = userEvent.setup();
    renderSection("2026-09-01T00:00:00.000Z");

    await user.click(screen.getByRole("button", { name: /cancel erasure request/i }));
    const dialog = screen.getByRole("alertdialog");
    await user.click(within(dialog).getByRole("button", { name: /cancel erasure request/i }));

    expect(await screen.findByText(/erasure request cancelled/i)).toBeInTheDocument();
    expect(cancelCalled).toBe(true);
  });

  it("dismissing the cancel dialog without confirming does not call the endpoint", async () => {
    const user = userEvent.setup();
    renderSection("2026-09-01T00:00:00.000Z");

    await user.click(screen.getByRole("button", { name: /cancel erasure request/i }));
    const dialog = screen.getByRole("alertdialog");
    await user.click(within(dialog).getByRole("button", { name: /keep erasure scheduled/i }));

    expect(cancelCalled).toBe(false);
  });

  it("shows an error toast when the cancel request fails", async () => {
    server.use(
      http.post(`http://localhost/api/v1/clients/${CLIENT_ID}/cancel-erasure`, () =>
        HttpResponse.json({ message: "Server error" }, { status: 500 })),
    );
    const user = userEvent.setup();
    renderSection("2026-09-01T00:00:00.000Z");

    await user.click(screen.getByRole("button", { name: /cancel erasure request/i }));
    const dialog = screen.getByRole("alertdialog");
    await user.click(within(dialog).getByRole("button", { name: /cancel erasure request/i }));

    expect(await screen.findByText(/couldn't cancel the erasure request/i)).toBeInTheDocument();
  });
});
