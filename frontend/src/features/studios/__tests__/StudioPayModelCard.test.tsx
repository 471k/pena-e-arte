import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { studiosApi, type StudioPayModel, type UpdateStudioPayModelRequest } from "@/features/studios/studiosApi";
import { StudioPayModelCard } from "@/features/studios/components/StudioPayModelCard";
import { parseRate } from "@/features/studios/payModel";

// A07a phase 4: the owner's pay-model card, mounted on its own (not through StudioProfilePage, whose
// whole-page tests are the suite's slowest).

// Registration's default: commission with no rate, nobody has reviewed it.
const DEFAULT_TERMS: StudioPayModel = {
  arrangement: "Commission", commissionRate: null, flatFeePerSession: null, reviewedAt: null, currency: "ALL",
};

let body: StudioPayModel = DEFAULT_TERMS;
let status = 200;
let saved: UpdateStudioPayModelRequest | null = null;

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me/pay-model", () =>
    status === 200 ? HttpResponse.json(body) : HttpResponse.json({ title: "boom" }, { status })),
  http.put("http://localhost/api/v1/studios/me/pay-model", async ({ request }) => {
    saved = (await request.json()) as UpdateStudioPayModelRequest;
    body = { ...saved, reviewedAt: "2026-11-11T10:00:00Z", currency: "ALL" };
    return HttpResponse.json(body);
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); body = DEFAULT_TERMS; status = 200; saved = null; });
afterAll(() => server.close());

function renderCard() {
  const store = configureStore({
    reducer: { auth: authReducer, ui: uiReducer, [studiosApi.reducerPath]: studiosApi.reducer },
    middleware: (gd) => gd().concat(studiosApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "owner@ink.test" }, token: "t", tenantId: "stud-0001", role: "owner", pendingReferralCode: null, impersonation: null } as any,
    },
  });
  render(<Provider store={store}><StudioPayModelCard /></Provider>);
}

describe("StudioPayModelCard", () => {
  it("shows a loading state while the terms load", () => {
    renderCard();
    expect(screen.getByTestId("pay-model-loading")).toBeInTheDocument();
  });

  it("says it couldn't load, with a retry, when the read fails", async () => {
    status = 500;
    renderCard();
    expect(await screen.findByRole("alert")).toHaveTextContent("We couldn't load how artists get paid.");
    expect(screen.getByRole("button", { name: /try again/i })).toBeInTheDocument();
  });

  it("renders an unset rate as unset, never as 0%, and says the default isn't reviewed", async () => {
    renderCard();
    const rate = await screen.findByLabelText(/Artist's share/);
    expect(rate).toHaveValue("");
    expect(screen.getByTestId("pay-model-rate-unset")).toHaveTextContent("No rate set yet");
    expect(screen.queryByText("0%")).not.toBeInTheDocument();
    expect(screen.getByTestId("pay-model-state")).toHaveAttribute("data-state", "DefaultUnreviewed");
    expect(screen.getByTestId("pay-model-state")).toHaveClass("text-warning-text");
  });

  it("shows a saved state once the owner has reviewed the terms", async () => {
    body = { ...DEFAULT_TERMS, commissionRate: 60, reviewedAt: "2026-11-01T10:00:00Z" };
    renderCard();
    const state = await screen.findByTestId("pay-model-state");
    expect(state).toHaveAttribute("data-state", "Reviewed");
    expect(state).toHaveTextContent("Saved 1 Nov 2026");
    expect(screen.getByLabelText(/Artist's share/)).toHaveValue("60");
  });

  it("asks for the rate only under commission and the fee only under a flat fee", async () => {
    const user = userEvent.setup();
    renderCard();
    const select = await screen.findByLabelText("Arrangement");
    expect(screen.getByLabelText(/Artist's share/)).toBeInTheDocument();
    expect(screen.queryByLabelText(/fee per session/)).not.toBeInTheDocument();

    await user.selectOptions(select, "FlatFeePerSession");
    expect(screen.queryByLabelText(/Artist's share/)).not.toBeInTheDocument();
    expect(screen.getByLabelText("The studio's fee per session (ALL)")).toBeInTheDocument();

    await user.selectOptions(select, "BoothRent");
    expect(screen.queryByLabelText(/Artist's share/)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/fee per session/)).not.toBeInTheDocument();
  });

  it("won't save commission without a rate", async () => {
    const user = userEvent.setup();
    renderCard();
    await user.click(await screen.findByRole("button", { name: "Save artist pay" }));
    expect(await screen.findByText("Set the commission rate.")).toBeInTheDocument();
    expect(saved).toBeNull();
  });

  it("saves the rate as the percentage typed", async () => {
    const user = userEvent.setup();
    renderCard();
    await user.type(await screen.findByLabelText(/Artist's share/), "62.5");
    await user.click(screen.getByRole("button", { name: "Save artist pay" }));
    expect(await screen.findByTestId("pay-model-state")).toHaveAttribute("data-state", "Reviewed");
    expect(saved).toEqual({ arrangement: "Commission", commissionRate: 62.5, flatFeePerSession: null });
  });

  it("keeps the explanation in the page, behind a real disclosure", async () => {
    const user = userEvent.setup();
    renderCard();
    const toggle = await screen.findByRole("button", { name: /How shares are worked out/ });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    const region = document.getElementById(toggle.getAttribute("aria-controls")!)!;
    expect(region).not.toBeVisible();
    expect(within(region).getByText(/before the card fee/)).toBeInTheDocument();
    await user.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(region).toBeVisible();
  });
});

describe("parseRate", () => {
  it.each([
    ["", null, null],
    ["60", 60, null],
    ["0.29", 0.29, null],
    ["62.5", 62.5, null],
    ["100.5", null, "Enter a percentage from 0 to 100."],
    ["60.125", null, "Use at most 2 decimals."],
    ["abc", null, "Enter a percentage from 0 to 100."],
  ])("%s", (input, value, error) => {
    expect(parseRate(input)).toEqual({ value, error });
  });
});
