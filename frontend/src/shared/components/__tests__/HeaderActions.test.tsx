import { describe, it, expect, beforeAll, afterEach, afterAll, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import authReducer from "@/features/auth/authSlice";
import { authApi } from "@/features/auth/authApi";
import { feedbackApi } from "@/features/feedback/feedbackApi";
import { helpApi } from "@/features/help/helpApi";
import { onboardingApi } from "@/features/help/onboardingApi";
import { messagingApi } from "@/features/messaging/messagingApi";
import { HeaderActions } from "../HeaderActions";
import { HEADER_MORE_TOUR_ID } from "@/features/help/tours/headerFold";
import { Role } from "@/shared/types/roles";

// The bell has its own slice and API and isn't what's under test; it only has to be there in both states.
vi.mock("@/features/notifications/components/NotificationBell", () => ({
  NotificationBell: () => <button type="button" aria-label="View notifications" />,
}));

let unread = 3;
const server = setupServer(
  http.get("http://localhost/api/v1/conversations/unread-count", () => HttpResponse.json(unread)),
  http.get("http://localhost/api/v1/onboarding/tour-status", () => HttpResponse.json({ hasCompletedTour: true })),
  http.post("http://localhost/api/v1/onboarding/tour-complete", () => new HttpResponse(null, { status: 204 })),
  http.get("http://localhost/api/v1/auth/my-studios", () => HttpResponse.json([])),
  http.get("http://localhost/api/v1/feedback/mine", () => HttpResponse.json([])),
  http.post("http://localhost/api/v1/help/search-log", () => new HttpResponse(null, { status: 204 })),
);
beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); unread = 3; });
afterAll(() => server.close());

function renderActions(props: { folded: boolean; feedback?: boolean; messages?: boolean; role?: Role }) {
  const onFeedback = vi.fn();
  const store = configureStore({
    reducer: {
      auth: authReducer,
      [helpApi.reducerPath]: helpApi.reducer,
      [onboardingApi.reducerPath]: onboardingApi.reducer,
      [authApi.reducerPath]: authApi.reducer,
      [feedbackApi.reducerPath]: feedbackApi.reducer,
      [messagingApi.reducerPath]: messagingApi.reducer,
    },
    middleware: (gd) => gd().concat(
      helpApi.middleware, onboardingApi.middleware, authApi.middleware, feedbackApi.middleware, messagingApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "o@test.com" }, token: "t", tenantId: "s1", role: props.role ?? Role.Owner } as any,
    },
  });
  render(
    <Provider store={store}>
      <MemoryRouter>
        <HeaderActions
          folded={props.folded}
          onFeedback={props.feedback === false ? undefined : onFeedback}
          showMessages={props.messages !== false}
          help={{ ownerTourFacts: { hasArtistProfile: true, showsPublishBanner: false, hasJoinInvite: false } }}
          userMenu={<button type="button" aria-label="User menu" />}
        />
      </MemoryRouter>
    </Provider>,
  );
  return { onFeedback };
}

describe("HeaderActions (S05: the header at 200% text)", () => {
  it("spaces its controls in px, so 200% text does not double the gaps (320px phone)", () => {
    renderActions({ folded: true });
    const row = screen.getByRole("button", { name: "User menu" }).parentElement!;
    expect(row.className).toContain("gap-[4px]");
    expect(row.className).not.toMatch(/(^| )gap-1( |$)/);
  });

  it("with room, every control is inline and there is no More menu", async () => {
    renderActions({ folded: false });

    expect(screen.getByRole("button", { name: "Send feedback" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Open help menu" })).toHaveAttribute("data-tour", "owner-help-button");
    expect(await screen.findByRole("link", { name: "View messages, 3 unread" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^More/ })).not.toBeInTheDocument();
  });

  it("folded, feedback, help and messages are in More, whose trigger carries the unread count", async () => {
    renderActions({ folded: true });

    const more = await screen.findByRole("button", { name: "More: help, messages and more, 3 unread messages" });
    expect(more).toHaveAttribute("data-tour", HEADER_MORE_TOUR_ID);
    expect(within(more).getByText("3")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Open help menu" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Send feedback" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /View messages/ })).not.toBeInTheDocument();
  });

  it("the bell and the account menu never fold", () => {
    renderActions({ folded: true });

    expect(screen.getByRole("button", { name: "View notifications" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "User menu" })).toBeInTheDocument();
  });

  it("with no unread messages the trigger carries no count", async () => {
    unread = 0;
    renderActions({ folded: true });

    const more = await screen.findByRole("button", { name: "More: help, messages and more" });
    expect(more.textContent).toBe("");
  });

  it("Help in More opens the Help sheet, and Messages names its unread count", async () => {
    const user = userEvent.setup();
    renderActions({ folded: true });

    await user.click(await screen.findByRole("button", { name: /^More/ }));
    expect(screen.getByRole("menuitem", { name: "Messages, 3 unread" })).toHaveAttribute("href", "/messages");
    await user.click(screen.getByRole("menuitem", { name: "Help" }));
    expect(await screen.findByRole("heading", { name: "Help" })).toBeInTheDocument();
  });

  it("Send feedback in More calls the layout's feedback dialog", async () => {
    const user = userEvent.setup();
    const { onFeedback } = renderActions({ folded: true });

    await user.click(await screen.findByRole("button", { name: /^More/ }));
    await user.click(screen.getByRole("menuitem", { name: "Send feedback" }));
    expect(onFeedback).toHaveBeenCalledTimes(1);
  });

  it("with Help as the only foldable control (admin), it stays inline even when folded", () => {
    renderActions({ folded: true, feedback: false, messages: false, role: Role.Admin });

    expect(screen.getByRole("button", { name: "Open help menu" })).toHaveAttribute("data-tour", "admin-help-button");
    expect(screen.queryByRole("button", { name: /^More/ })).not.toBeInTheDocument();
  });
});
