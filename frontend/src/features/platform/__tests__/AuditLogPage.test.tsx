import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { platformApi } from "@/features/platform/platformApi";
import { AuditLogPage } from "@/features/platform/components/AuditLogPage";
import type { AuditLogPageResponse } from "@/features/platform/platform.types";

const PAGE: AuditLogPageResponse = {
  items: [
    {
      id: "log-1", actorUserId: "u-1", actorRole: "issuer",
      action: "Studio.Suspended", targetType: "Studio", targetId: "s-001",
      studioId: "s-001", metadata: "{}", createdAt: "2026-07-20T10:00:00Z",
    },
    {
      id: "log-2", actorUserId: "u-2", actorRole: "issuer",
      action: "Plan.Updated", targetType: "Plan", targetId: "p-001",
      studioId: null, metadata: "{}", createdAt: "2026-07-19T10:00:00Z",
    },
  ],
  totalCount: 2,
  page: 1,
  pageSize: 50,
};

const server = setupServer(
  http.get("http://localhost/api/v1/platform/audit-log", () => HttpResponse.json(PAGE)),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth:                      authReducer,
      [platformApi.reducerPath]: platformApi.reducer,
    },
    middleware: (gd) => gd().concat(platformApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u4", email: "issuer@platform.test" }, token: "fake", tenantId: null, role: "issuer" } as any,
    },
  });
}

function renderPage() {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter>
        <AuditLogPage />
      </MemoryRouter>
    </Provider>,
  );
}

describe("AuditLogPage", () => {
  it("renders the header", () => {
    renderPage();
    expect(screen.getByText("Audit Log")).toBeInTheDocument();
  });

  it("renders entries with action and target", async () => {
    renderPage();
    expect(await screen.findByText("Studio.Suspended")).toBeInTheDocument();
    expect(screen.getByText("Plan.Updated")).toBeInTheDocument();
  });

  it("shows 'Platform-wide' for an entry with a null studioId", async () => {
    renderPage();
    await screen.findByText("Plan.Updated");
    expect(screen.getByText("Platform-wide")).toBeInTheDocument();
  });

  it("shows the total count in the header badge", async () => {
    renderPage();
    expect(await screen.findByText("2")).toBeInTheDocument();
  });

  it("shows an error message when the request fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/audit-log", () =>
        HttpResponse.json({ message: "fail" }, { status: 500 }),
      ),
    );
    renderPage();
    await waitFor(() => expect(screen.getByText(/failed to load the audit log/i)).toBeInTheDocument());
  });

  it("retries the request when 'Try again' is clicked", async () => {
    let requestCount = 0;
    server.use(
      http.get("http://localhost/api/v1/platform/audit-log", () => {
        requestCount += 1;
        return requestCount === 1
          ? HttpResponse.json({ message: "fail" }, { status: 500 })
          : HttpResponse.json(PAGE);
      }),
    );
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /try again/i }));
    expect(await screen.findByText("Studio.Suspended")).toBeInTheDocument();
  });

  it("shows an empty-state message when there are no entries", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/audit-log", () =>
        HttpResponse.json({ items: [], totalCount: 0, page: 1, pageSize: 50 }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/no audit log entries match/i)).toBeInTheDocument();
  });

  it("filtering by action refetches with the action query param", async () => {
    let lastUrl = "";
    server.use(
      http.get("http://localhost/api/v1/platform/audit-log", ({ request }) => {
        lastUrl = request.url;
        return HttpResponse.json(PAGE);
      }),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Studio.Suspended");
    await user.type(screen.getByLabelText(/^action$/i), "Studio.Suspended");
    await waitFor(() => expect(lastUrl).toContain("action=Studio.Suspended"));
  });
});
