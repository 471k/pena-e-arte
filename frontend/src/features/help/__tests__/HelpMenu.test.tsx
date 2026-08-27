import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import authReducer from "@/features/auth/authSlice";
import { authApi } from "@/features/auth/authApi";
import { feedbackApi } from "@/features/feedback/feedbackApi";
import { helpApi } from "../helpApi";
import { onboardingApi } from "../onboardingApi";
import { HelpMenu } from "../components/HelpMenu";
import type { Role } from "@/shared/types/roles";

const server = setupServer(
  http.post("http://localhost/api/v1/help/search-log", () => new HttpResponse(null, { status: 204 })),
  // Tour already completed by default — keeps it out of the way of unrelated assertions.
  http.get("http://localhost/api/v1/onboarding/tour-status", () => HttpResponse.json({ hasCompletedTour: true })),
  http.post("http://localhost/api/v1/onboarding/tour-complete", () => new HttpResponse(null, { status: 204 })),
  http.get("http://localhost/api/v1/auth/my-studios", () => HttpResponse.json([])),
  http.get("http://localhost/api/v1/feedback/mine", () => HttpResponse.json([])),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

function makeStore(role: Role) {
  return configureStore({
    reducer: {
      auth: authReducer,
      [helpApi.reducerPath]: helpApi.reducer,
      [onboardingApi.reducerPath]: onboardingApi.reducer,
      [authApi.reducerPath]: authApi.reducer,
      [feedbackApi.reducerPath]: feedbackApi.reducer,
    },
    middleware: (gd) => gd().concat(helpApi.middleware, onboardingApi.middleware, authApi.middleware, feedbackApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "test@test.com" }, token: "fake", tenantId: "t1", role } as any,
    },
  });
}

function renderMenu(role: Role = "client" as Role) {
  const store = makeStore(role);
  render(
    <Provider store={store}>
      <MemoryRouter>
        <HelpMenu />
      </MemoryRouter>
    </Provider>,
  );
}

describe("HelpMenu", () => {
  it("opens the sheet when the help button is clicked", async () => {
    const user = userEvent.setup();
    renderMenu();

    await user.click(screen.getByRole("button", { name: /open help menu/i }));

    expect(screen.getByRole("heading", { name: /help/i })).toBeInTheDocument();
  });

  it("opens the sheet on Shift+? when no input is focused", async () => {
    const user = userEvent.setup();
    renderMenu();

    document.body.focus();
    await user.keyboard("{Shift>}?{/Shift}");

    expect(screen.getByRole("heading", { name: /help/i })).toBeInTheDocument();
  });

  it("does not open on Shift+? while typing in a text field", async () => {
    const user = userEvent.setup();
    render(
      <Provider store={makeStore("client" as Role)}>
        <MemoryRouter>
          <input aria-label="some other field" />
          <HelpMenu />
        </MemoryRouter>
      </Provider>,
    );

    await user.click(screen.getByLabelText(/some other field/i));
    await user.keyboard("{Shift>}?{/Shift}");

    expect(screen.queryByRole("heading", { name: /^help$/i })).not.toBeInTheDocument();
  });

  it("only shows articles matching the current role", async () => {
    const user = userEvent.setup();
    renderMenu("client" as Role);

    await user.click(screen.getByRole("button", { name: /open help menu/i }));

    expect(screen.getByText(/book an appointment/i)).toBeInTheDocument();
    expect(screen.queryByText(/manage subscription plans/i)).not.toBeInTheDocument();
  });

  it("shows the all-roles toggle only for issuer", async () => {
    const user = userEvent.setup();
    renderMenu("issuer" as Role);

    await user.click(screen.getByRole("button", { name: /open help menu/i }));

    expect(screen.getByRole("button", { name: /show all roles' guides/i })).toBeInTheDocument();
  });

  it("does not show the all-roles toggle for non-issuer roles", async () => {
    const user = userEvent.setup();
    renderMenu("owner" as Role);

    await user.click(screen.getByRole("button", { name: /open help menu/i }));

    expect(screen.queryByRole("button", { name: /show all roles' guides/i })).not.toBeInTheDocument();
  });

  it("navigates and closes the sheet when Go to this page is clicked", async () => {
    const user = userEvent.setup();
    renderMenu("client" as Role);

    await user.click(screen.getByRole("button", { name: /open help menu/i }));
    await user.click(screen.getByText(/book an appointment/i));
    await user.click(screen.getByRole("button", { name: /go to this page/i }));

    expect(screen.queryByRole("heading", { name: /^help$/i })).not.toBeInTheDocument();
  });

  it("narrows results as the user searches", async () => {
    const user = userEvent.setup();
    renderMenu("client" as Role);

    await user.click(screen.getByRole("button", { name: /open help menu/i }));
    await user.type(screen.getByLabelText(/search help/i), "body map");

    expect(screen.getByText(/update your profile and body map/i)).toBeInTheDocument();
    expect(screen.queryByText(/book an appointment/i)).not.toBeInTheDocument();
  });

  it("logs the search exactly once after the debounce delay for a distinct query", async () => {
    const user = userEvent.setup();
    const captured: unknown[] = [];
    server.use(
      http.post("http://localhost/api/v1/help/search-log", async ({ request }) => {
        captured.push(await request.json());
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderMenu("client" as Role);

    await user.click(screen.getByRole("button", { name: /open help menu/i }));
    await user.type(screen.getByLabelText(/search help/i), "password");

    await waitFor(() => expect(captured).toHaveLength(1), { timeout: 3000 });
    expect(captured[0]).toMatchObject({ query: "password" });
  }, 10000);

  it("does not log the same distinct query twice in one open session", async () => {
    const user = userEvent.setup();
    let callCount = 0;
    server.use(
      http.post("http://localhost/api/v1/help/search-log", () => {
        callCount++;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderMenu("client" as Role);

    await user.click(screen.getByRole("button", { name: /open help menu/i }));
    await user.type(screen.getByLabelText(/search help/i), "password");
    await waitFor(() => expect(callCount).toBe(1), { timeout: 3000 });

    // Clear and retype the same query — still within the same open session.
    await user.clear(screen.getByLabelText(/search help/i));
    await user.type(screen.getByLabelText(/search help/i), "password");
    await new Promise((r) => setTimeout(r, 1000));

    expect(callCount).toBe(1);
  }, 10000);

  it("'Take the tour again' closes the sheet and relaunches the tour even though it was already completed", async () => {
    // The owner tour's earlier steps (9 of them, as of the solo-studio-publish-banner
    // and join-invite-bell steps added alongside the solo-artist feature) target nav
    // elements that this isolated render doesn't include, so the tour auto-skips
    // through them before reaching the last step, "owner-help-button" — the trigger
    // button HelpMenu itself renders, which does resolve. Each skip polls up to
    // MAX_POLL_ATTEMPTS (20) x POLL_INTERVAL_MS (50) = ~1s (OnboardingTour.tsx)
    // before giving up, so 9 steps is a ~9s floor before real overhead (RAF
    // double-buffering, React commit time) on top — timeouts below carry generous
    // margin above that, not just the bare theoretical floor.
    const user = userEvent.setup();
    renderMenu("owner" as Role);

    await user.click(screen.getByRole("button", { name: /open help menu/i }));
    await user.click(screen.getByRole("button", { name: /take the tour again/i }));

    expect(screen.queryByRole("heading", { name: /^help$/i })).not.toBeInTheDocument();
    expect(await screen.findByRole("dialog", {}, { timeout: 16000 })).toBeInTheDocument();
  }, 22000);

  it("Contact Support tab shows the request form when there is no open ticket", async () => {
    const user = userEvent.setup();
    renderMenu("client" as Role);

    await user.click(screen.getByRole("button", { name: /open help menu/i }));
    await user.click(screen.getByRole("tab", { name: /contact support/i }));

    expect(await screen.findByLabelText(/subject/i)).toBeInTheDocument();
  });

  it("Contact Support tab shows the ticket thread when an open ticket exists", async () => {
    server.use(
      http.get("http://localhost/api/v1/feedback/mine", () => HttpResponse.json([{
        id: "fb-1", type: "SupportRequest", title: "Need help", body: "Can't find billing.",
        status: "Open", studioName: "Ink Soul", submitterRole: "client",
        issuerNote: null, createdAt: "2026-07-21T00:00:00.000Z", resolvedAt: null,
      }])),
      http.get("http://localhost/api/v1/feedback/fb-1/messages", () => HttpResponse.json([])),
    );
    const user = userEvent.setup();
    renderMenu("client" as Role);

    await user.click(screen.getByRole("button", { name: /open help menu/i }));
    await user.click(screen.getByRole("tab", { name: /contact support/i }));

    expect(await screen.findByText("Need help")).toBeInTheDocument();
    expect(screen.queryByLabelText(/subject/i)).not.toBeInTheDocument();
  });
});
