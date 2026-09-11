import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { PokSettingsCard } from "@/features/studios/components/PokSettingsCard";
import type { PokConnectionStatusResponse } from "@/features/payments/payment.types";

const NOT_CONNECTED: PokConnectionStatusResponse = { connected: false, merchantId: null };
const CONNECTED: PokConnectionStatusResponse = { connected: true, merchantId: "merchant-abc" };

const server = setupServer(
  http.get("http://localhost/api/v1/payments/pok/connection", () => HttpResponse.json(NOT_CONNECTED)),
  http.post("http://localhost/api/v1/payments/pok/connect", () => new HttpResponse(null, { status: 200 })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth:                      authReducer,
      [paymentsApi.reducerPath]: paymentsApi.reducer,
    },
    middleware: (gd) => gd().concat(paymentsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "owner@test.com" }, token: "fake", tenantId: "t1", role: "Owner" } as any,
    },
  });
}

function renderCard() {
  render(
    <Provider store={makeStore()}>
      <PokSettingsCard />
    </Provider>,
  );
}

describe("PokSettingsCard", () => {
  it("shows the connect form when no account is connected", async () => {
    renderCard();
    expect(await screen.findByLabelText(/key id/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/key secret/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/merchant id/i)).toBeInTheDocument();
  });

  it("connect button is disabled until all three fields are filled", async () => {
    const user = userEvent.setup();
    renderCard();
    const connectButton = await screen.findByRole("button", { name: /connect/i });
    expect(connectButton).toBeDisabled();

    await user.type(screen.getByLabelText(/key id/i), "key-1");
    await user.type(screen.getByLabelText(/key secret/i), "secret-1");
    await user.type(screen.getByLabelText(/merchant id/i), "merchant-1");

    expect(connectButton).toBeEnabled();
  });

  it("submits credentials and shows the connected state on success", async () => {
    const user = userEvent.setup();
    let posted: unknown;
    let getCalls = 0;
    server.use(
      http.post("http://localhost/api/v1/payments/pok/connect", async ({ request }) => {
        posted = await request.json();
        return new HttpResponse(null, { status: 200 });
      }),
      // First call (initial mount) still reports NOT_CONNECTED — only the refetch that
      // invalidatesTags triggers after a successful connect should reflect CONNECTED, so the
      // test proves the UI reflects a real server round-trip, not just optimistic local state.
      http.get("http://localhost/api/v1/payments/pok/connection", () => {
        getCalls += 1;
        return HttpResponse.json(getCalls > 1 ? CONNECTED : NOT_CONNECTED);
      }),
    );
    renderCard();

    await user.type(await screen.findByLabelText(/key id/i), "key-1");
    await user.type(screen.getByLabelText(/key secret/i), "secret-1");
    await user.type(screen.getByLabelText(/merchant id/i), "merchant-1");
    await user.click(screen.getByRole("button", { name: /connect/i }));

    expect(await screen.findByText(/connected/i)).toBeInTheDocument();
    expect(await screen.findByText(/merchant-abc/i)).toBeInTheDocument();
    expect(posted).toEqual({ keyId: "key-1", keySecret: "secret-1", merchantId: "merchant-1" });
  });

  it("shows the connected state with the merchant id and no credential fields", async () => {
    server.use(http.get("http://localhost/api/v1/payments/pok/connection", () => HttpResponse.json(CONNECTED)));
    renderCard();

    expect(await screen.findByText(/connected/i)).toBeInTheDocument();
    expect(screen.getByText(/merchant-abc/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/key secret/i)).not.toBeInTheDocument();
  });

  it("shows the backend error message when connecting fails", async () => {
    const user = userEvent.setup();
    server.use(
      http.post("http://localhost/api/v1/payments/pok/connect", () =>
        HttpResponse.json({ status: 422, message: "Invalid credentials." }, { status: 422 }),
      ),
    );
    renderCard();

    await user.type(await screen.findByLabelText(/key id/i), "key-1");
    await user.type(screen.getByLabelText(/key secret/i), "secret-1");
    await user.type(screen.getByLabelText(/merchant id/i), "merchant-1");
    await user.click(screen.getByRole("button", { name: /connect/i }));

    expect(await screen.findByText(/invalid credentials/i)).toBeInTheDocument();
  });
});
