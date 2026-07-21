import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import authReducer from "@/features/auth/authSlice";
import { helpApi } from "../helpApi";
import { HelpMenu } from "../components/HelpMenu";
import type { Role } from "@/shared/types/roles";

const server = setupServer(
  http.post("http://localhost/api/v1/help/search-log", () => new HttpResponse(null, { status: 204 })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

function makeStore(role: Role) {
  return configureStore({
    reducer: { auth: authReducer, [helpApi.reducerPath]: helpApi.reducer },
    middleware: (gd) => gd().concat(helpApi.middleware),
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
});
