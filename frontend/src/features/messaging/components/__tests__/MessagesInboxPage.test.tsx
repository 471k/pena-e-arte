import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { messagingApi } from "../../messagingApi";
import { MessagesInboxPage } from "../MessagesInboxPage";
import { Role } from "@/shared/types/roles";

const server = setupServer(
  http.get("http://localhost/api/v1/conversations", () =>
    HttpResponse.json([]),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function makeStore(role: Role) {
  return configureStore({
    reducer: { auth: authReducer, [messagingApi.reducerPath]: messagingApi.reducer },
    middleware: (gd) => gd().concat(messagingApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "test@test.com" }, token: "fake", tenantId: "t1", role } as any,
    },
  });
}

function renderInbox(role: Role) {
  render(
    <Provider store={makeStore(role)}>
      <MemoryRouter initialEntries={["/messages"]}>
        <MessagesInboxPage />
      </MemoryRouter>
    </Provider>,
  );
}

describe("MessagesInboxPage", () => {
  it.each([Role.Client, Role.Artist, Role.Owner])(
    "shows an empty state when there are no conversations yet (%s)",
    async (role) => {
      renderInbox(role);

      expect(await screen.findByText(/no conversations yet/i)).toBeInTheDocument();
    },
  );
});
