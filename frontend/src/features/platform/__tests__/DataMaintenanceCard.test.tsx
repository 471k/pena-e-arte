import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { platformApi } from "@/features/platform/platformApi";
import { DataMaintenanceCard } from "@/features/platform/components/DataMaintenanceCard";

const BILLED_URL = "http://localhost/api/v1/platform/subscriptions/backfill-billed-amounts";
const LEDGER_URL = "http://localhost/api/v1/platform/subscriptions/backfill-revenue-ledger";

const CASH_STUDIO_ID = "5b0f6a3e-1111-4222-8333-444455556666";

const server = setupServer();

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function renderCard() {
  const store = configureStore({
    reducer: { auth: authReducer, [platformApi.reducerPath]: platformApi.reducer },
    middleware: (gd) => gd().concat(platformApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u4", email: "admin@platform.test" }, token: "fake", tenantId: null, role: "admin", pendingReferralCode: null, impersonation: null } as any,
    },
  });
  render(
    <Provider store={store}>
      <MemoryRouter>
        <DataMaintenanceCard />
      </MemoryRouter>
    </Provider>,
  );
}

function trackRequests() {
  const calls: string[] = [];
  server.use(
    http.post(BILLED_URL, () => {
      calls.push("billed");
      return HttpResponse.json({
        cardBilledUpdated: 3,
        cardBilledSkipped: 1,
        cashBilledSnapshots: [{ studioId: CASH_STUDIO_ID, price: 59 }],
      });
    }),
    http.post(LEDGER_URL, () => {
      calls.push("ledger");
      return HttpResponse.json({ created: 9, skippedAlreadyInLedger: 0, skippedNotBilling: 16 });
    }),
  );
  return calls;
}

describe("DataMaintenanceCard", () => {
  it("lists both backfills in order, billed amounts first, and fires nothing on render", () => {
    const calls = trackRequests();
    renderCard();

    const items = screen.getAllByRole("listitem");
    expect(items).toHaveLength(2);
    expect(within(items[0]).getByText(/billed-amount snapshot/i)).toBeInTheDocument();
    expect(within(items[0]).getByText(/step 1/i)).toBeInTheDocument();
    expect(within(items[1]).getByText(/revenue ledger/i)).toBeInTheDocument();
    expect(within(items[1]).getByText(/step 2/i)).toBeInTheDocument();
    expect(calls).toEqual([]);
  });

  it("asks for confirmation before writing, and Cancel sends nothing", async () => {
    const user = userEvent.setup();
    const calls = trackRequests();
    renderCard();

    await user.click(screen.getByRole("button", { name: /run: billed-amount snapshot/i }));
    expect(screen.getByText(/this writes to the database/i)).toBeInTheDocument();
    expect(calls).toEqual([]);

    await user.click(screen.getByRole("button", { name: /^cancel$/i }));
    expect(screen.queryByText(/this writes to the database/i)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /run: billed-amount snapshot/i })).toBeInTheDocument();
    expect(calls).toEqual([]);
  });

  it("billed-amount backfill: confirm posts once and shows counts plus the cash-billed review list", async () => {
    const user = userEvent.setup();
    const calls = trackRequests();
    renderCard();

    await user.click(screen.getByRole("button", { name: /run: billed-amount snapshot/i }));
    await user.click(screen.getByRole("button", { name: /^confirm$/i }));

    const result = await screen.findByRole("status");
    expect(result).toHaveTextContent("3 card-billed updated, 1 skipped (not found in Stripe), 1 cash-billed snapshotted.");
    expect(calls).toEqual(["billed"]);

    const link = screen.getByRole("link", { name: /studio 5b0f6a3e/i });
    expect(link).toHaveAttribute("href", `/platform/studios/${CASH_STUDIO_ID}`);
    expect(screen.getByText(/€59\.00\/mo/)).toBeInTheDocument();
  });

  it("revenue-ledger backfill: confirm posts once and shows the returned counts", async () => {
    const user = userEvent.setup();
    const calls = trackRequests();
    renderCard();

    await user.click(screen.getByRole("button", { name: /run: revenue ledger/i }));
    await user.click(screen.getByRole("button", { name: /^confirm$/i }));

    const result = await screen.findByRole("status");
    expect(result).toHaveTextContent("9 subscriptions seeded, 0 already in the ledger, 16 not currently billing.");
    expect(calls).toEqual(["ledger"]);
  });

  it("a rerun that creates nothing says so instead of looking like a failure", async () => {
    server.use(
      http.post(LEDGER_URL, () =>
        HttpResponse.json({ created: 0, skippedAlreadyInLedger: 9, skippedNotBilling: 16 }),
      ),
    );
    const user = userEvent.setup();
    renderCard();

    await user.click(screen.getByRole("button", { name: /run: revenue ledger/i }));
    await user.click(screen.getByRole("button", { name: /^confirm$/i }));

    expect(await screen.findByText(/nothing new to seed/i)).toBeInTheDocument();
  });

  it("a failed run shows no result and lets the admin try again", async () => {
    server.use(http.post(LEDGER_URL, () => HttpResponse.json({ error: "boom" }, { status: 500 })));
    const user = userEvent.setup();
    renderCard();

    await user.click(screen.getByRole("button", { name: /run: revenue ledger/i }));
    await user.click(screen.getByRole("button", { name: /^confirm$/i }));

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /run: revenue ledger/i })).toBeEnabled(),
    );
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });
});
