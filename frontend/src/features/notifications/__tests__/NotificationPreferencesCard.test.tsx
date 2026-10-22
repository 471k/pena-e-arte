import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { Toaster } from "sonner";

import authReducer from "@/features/auth/authSlice";
import { notificationsApi } from "@/features/notifications/notificationsApi";
import { NotificationPreferencesCard } from "@/features/notifications/components/NotificationPreferencesCard";
import type { NotificationPreferenceItem } from "@/features/notifications/notification.types";

let saved: NotificationPreferenceItem[] | null = null;

const server = setupServer(
  http.get("http://localhost/api/v1/notifications/preferences", () =>
    HttpResponse.json({ preferences: [] })),
  http.put("http://localhost/api/v1/notifications/preferences", async ({ request }) => {
    saved = ((await request.json()) as { preferences: NotificationPreferenceItem[] }).preferences;
    return new HttpResponse(null, { status: 204 });
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); saved = null; });
afterAll(() => server.close());

function renderCard() {
  const store = configureStore({
    reducer: { auth: authReducer, [notificationsApi.reducerPath]: notificationsApi.reducer },
    middleware: (gd) => gd().concat(notificationsApi.middleware),
  });
  render(
    <Provider store={store}>
      <Toaster />
      <NotificationPreferencesCard />
    </Provider>,
  );
}

describe("NotificationPreferencesCard", () => {
  it("offers the hold-ended email to the owner, with no SMS toggle (C-15)", async () => {
    renderCard();

    const row = (await screen.findByText("Card deposit hold ended (to you)")).closest("tr")!;
    expect(within(row).getByRole("switch", { name: /via Email/i })).toBeInTheDocument();
    expect(within(row).queryByRole("switch", { name: /via SMS/i })).not.toBeInTheDocument();
    expect(within(row).getByLabelText(/not sent by SMS/i)).toBeInTheDocument();
  });

  it("still shows both channels for client-facing events", async () => {
    renderCard();

    const row = (await screen.findByText("Deposit captured")).closest("tr")!;
    expect(within(row).getByRole("switch", { name: /via Email/i })).toBeInTheDocument();
    expect(within(row).getByRole("switch", { name: /via SMS/i })).toBeInTheDocument();
  });

  it("saves the owner's choice for the hold-ended email and never an SMS preference for it", async () => {
    renderCard();
    const user = userEvent.setup();

    await user.click(await screen.findByRole("switch", { name: "Card deposit hold ended (to you) via Email" }));
    await user.click(screen.getByRole("button", { name: /save notification settings/i }));

    await waitFor(() => expect(saved).not.toBeNull());
    const holdLost = saved!.filter((p) => p.type === "DepositHoldLost");
    expect(holdLost).toEqual([{ type: "DepositHoldLost", channel: "Email", isEnabled: false }]);
  });
});
