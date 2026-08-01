import { describe, it, expect, beforeAll, afterEach, afterAll, vi } from "vitest";
import { render, screen, cleanup, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { clientsApi } from "@/features/clients/clientsApi";
import { DeleteAccountSection } from "@/features/clients/components/DeleteAccountSection";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

let eraseCalled = false;
const server = setupServer(
  http.post("*/api/v1/clients/me/erase-data", () => {
    eraseCalled = true;
    return new HttpResponse(null, { status: 204 });
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  server.resetHandlers();
  cleanup();
  eraseCalled = false;
});
afterAll(() => server.close());

function renderSection() {
  const store = configureStore({
    reducer: { auth: authReducer, [clientsApi.reducerPath]: clientsApi.reducer },
    middleware: (getDefault) => getDefault().concat(clientsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "c@test.com" }, token: "t", tenantId: "s1", role: "client", pendingReferralCode: null } as any,
    },
  });
  render(
    <Provider store={store}>
      <MemoryRouter>
        <DeleteAccountSection />
      </MemoryRouter>
    </Provider>,
  );
}

describe("DeleteAccountSection", () => {
  it("requires typing DELETE before the confirm button is enabled, then calls the erase API", async () => {
    const user = userEvent.setup();
    renderSection();

    await user.click(screen.getByRole("button", { name: /delete my account/i }));

    const dialog = await screen.findByRole("dialog");
    const confirmButton = within(dialog).getByRole("button", { name: /delete my account/i });
    expect(confirmButton).toBeDisabled();
    expect(eraseCalled).toBe(false);

    await user.type(within(dialog).getByLabelText(/type .* to confirm/i), "DELETE");
    expect(confirmButton).toBeEnabled();

    await user.click(confirmButton);
    await waitFor(() => expect(eraseCalled).toBe(true));
  });

  it("does not call the API when confirmation text is wrong", async () => {
    const user = userEvent.setup();
    renderSection();

    await user.click(screen.getByRole("button", { name: /delete my account/i }));
    const dialog = await screen.findByRole("dialog");
    await user.type(within(dialog).getByLabelText(/type .* to confirm/i), "delete"); // wrong case

    expect(within(dialog).getByRole("button", { name: /delete my account/i })).toBeDisabled();
    expect(eraseCalled).toBe(false);
  });
});
