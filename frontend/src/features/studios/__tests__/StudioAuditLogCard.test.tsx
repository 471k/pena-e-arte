import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { studiosApi } from "@/features/studios/studiosApi";
import { StudioAuditLogCard } from "@/features/studios/components/StudioAuditLogCard";
import type { AuditLogPageResponse } from "@/features/platform/platform.types";

const PAGE: AuditLogPageResponse = {
  items: [
    {
      id: "log-1", actorUserId: "u-1", actorRole: "owner",
      action: "Appointment.Cancelled", targetType: "Appointment", targetId: "a-001",
      studioId: "s-001", metadata: "{}", createdAt: "2026-07-20T10:00:00Z",
    },
  ],
  totalCount: 1,
  page: 1,
  pageSize: 10,
};

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me/audit-log", () => HttpResponse.json(PAGE)),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth:                     authReducer,
      [studiosApi.reducerPath]: studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(studiosApi.middleware),
  });
}

function renderCard() {
  render(
    <Provider store={makeStore()}>
      <StudioAuditLogCard />
    </Provider>,
  );
}

describe("StudioAuditLogCard", () => {
  it("renders the card title", () => {
    renderCard();
    expect(screen.getByText("Recent studio activity")).toBeInTheDocument();
  });

  it("renders a recent action", async () => {
    renderCard();
    expect(await screen.findByText("Appointment.Cancelled")).toBeInTheDocument();
  });

  it("shows an empty-state message when there are no entries", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/me/audit-log", () =>
        HttpResponse.json({ items: [], totalCount: 0, page: 1, pageSize: 10 }),
      ),
    );
    renderCard();
    expect(await screen.findByText("No recorded actions yet.")).toBeInTheDocument();
  });

  it("shows an error message when the request fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/me/audit-log", () =>
        HttpResponse.json({ message: "fail" }, { status: 500 }),
      ),
    );
    renderCard();
    expect(await screen.findByText("Failed to load recent activity.")).toBeInTheDocument();
  });
});
