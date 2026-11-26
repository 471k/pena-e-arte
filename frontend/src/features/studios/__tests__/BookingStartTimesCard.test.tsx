import { describe, it, expect, beforeAll, afterEach, afterAll, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import authReducer from "@/features/auth/authSlice";
import { studiosApi } from "@/features/studios/studiosApi";
import { BookingStartTimesCard } from "@/features/studios/components/BookingStartTimesCard";
import { billingApi } from "@/features/billing/billingApi";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const URL = "http://localhost/api/v1/studios/me/booking-settings";
let saved: unknown = null;

const server = setupServer(
  http.get(URL, () => HttpResponse.json({ slotStepMinutes: null, allowedStepMinutes: [15, 30, 60] })),
  http.put(URL, async ({ request }) => {
    saved = await request.json();
    return HttpResponse.json({ ...(saved as object), allowedStepMinutes: [15, 30, 60] });
  }),
  // SubscriptionGatedButton reads the subscription and the studio (D05 phase 4b): an active owner by default.
  http.get("http://localhost/api/v1/billing/subscription", () => HttpResponse.json({ status: "Active" })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); saved = null; document.body.style.pointerEvents = ""; });
afterAll(() => server.close());

function renderCard() {
  const store = configureStore({
    reducer: {
      [billingApi.reducerPath]: billingApi.reducer, auth: authReducer, [studiosApi.reducerPath]: studiosApi.reducer },
    middleware: (gd) => gd().concat(billingApi.middleware, studiosApi.middleware),
  });
  render(<Provider store={store}><BookingStartTimesCard /></Provider>);
}

describe("BookingStartTimesCard (C03 follow-up)", () => {
  it("shows the current choice, every session length by default, with nothing to save yet", async () => {
    renderCard();
    expect(await screen.findByRole("combobox", { name: /offer start times/i })).toHaveTextContent("Every session length (default)");
    expect(screen.getByRole("button", { name: /save start times/i })).toBeDisabled();
  });

  it("saving every 30 minutes sends 30", async () => {
    const user = userEvent.setup();
    renderCard();
    await user.click(await screen.findByRole("combobox", { name: /offer start times/i }));
    await user.click(await screen.findByRole("option", { name: "Every 30 minutes" }));
    await user.click(screen.getByRole("button", { name: /save start times/i }));

    await waitFor(() => expect(saved).toEqual({ slotStepMinutes: 30 }));
  });

  it("a failed read says so with Try again, never a default it didn't load", async () => {
    server.use(http.get(URL, () => HttpResponse.json({ message: "fail" }, { status: 500 })));
    renderCard();
    expect(await screen.findByText(/couldn.t load this setting/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /try again/i })).toBeInTheDocument();
    expect(screen.queryByText("Every session length (default)")).not.toBeInTheDocument();
  });
});
