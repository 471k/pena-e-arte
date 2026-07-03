import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { toast } from "sonner";

import authReducer from "@/features/auth/authSlice";
import { feedbackApi } from "@/features/feedback/feedbackApi";
import { FeedbackInboxPage } from "@/features/feedback/components/FeedbackInboxPage";
import type { FeedbackReportResponse } from "@/features/feedback/feedback.types";

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));

const REPORTS: FeedbackReportResponse[] = [
  {
    id: "fb-1", type: "BugReport", title: "Broken button", body: "The submit button does nothing on Safari.",
    status: "Open", studioName: "Studio A", submitterRole: "artist",
    issuerNote: null, createdAt: "2026-07-01T00:00:00.000Z", resolvedAt: null,
  },
  {
    id: "fb-2", type: "FeatureRequest", title: "Add dark mode", body: "It would be great to have a dark theme.",
    status: "Resolved", studioName: "Studio B", submitterRole: "owner",
    issuerNote: "Shipped in v2", createdAt: "2026-06-28T00:00:00.000Z", resolvedAt: "2026-06-29T00:00:00.000Z",
  },
];

let lastUrl: URL | null = null;

const server = setupServer(
  http.get("http://localhost/api/v1/platform/feedback", ({ request }) => {
    lastUrl = new URL(request.url);
    return HttpResponse.json(REPORTS);
  }),
  http.patch("http://localhost/api/v1/platform/feedback/:id/status", ({ params }) =>
    HttpResponse.json({ ...REPORTS[0], id: params.id as string, status: "Reviewing" }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth:                      authReducer,
      [feedbackApi.reducerPath]: feedbackApi.reducer,
    },
    middleware: (gd) => gd().concat(feedbackApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "issuer@test.com" }, token: "fake", tenantId: null, role: "issuer" } as any,
    },
  });
}

function renderPage() {
  const store = makeStore();
  render(
    <Provider store={store}>
      <FeedbackInboxPage />
    </Provider>,
  );
}

describe("FeedbackInboxPage", () => {
  it("renders loading skeletons while isLoading", () => {
    server.use(
      http.get("http://localhost/api/v1/platform/feedback", async () => {
        await new Promise((r) => setTimeout(r, 50));
        return HttpResponse.json(REPORTS);
      }),
    );
    renderPage();

    expect(document.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  });

  it("renders the feedback card list on success", async () => {
    renderPage();

    expect(await screen.findByText("Broken button")).toBeInTheDocument();
    expect(screen.getByText("Add dark mode")).toBeInTheDocument();
  });

  it("shows the Bug Report badge for a BugReport entry", async () => {
    renderPage();

    expect(await screen.findByText("Bug Report")).toBeInTheDocument();
  });

  it("shows the correct status badge per entry", async () => {
    renderPage();

    expect(await screen.findByText("Open")).toBeInTheDocument();
    expect(screen.getByText("Resolved")).toBeInTheDocument();
  });

  it("filtering by type chip updates the RTK Query params", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Broken button");

    await user.click(screen.getByRole("button", { name: /^bug report$/i }));

    await waitFor(() => expect(lastUrl?.searchParams.get("type")).toBe("BugReport"));
  });

  it("filtering by status chip updates the RTK Query params", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Broken button");

    await user.click(screen.getByRole("button", { name: /^resolved$/i }));

    await waitFor(() => expect(lastUrl?.searchParams.get("status")).toBe("Resolved"));
  });

  it("expanding a card shows the full body, note textarea, and status buttons", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Broken button");

    const toggle = screen.getByRole("button", { name: /broken button/i, expanded: false });
    await user.click(toggle);
    const card = within(toggle.parentElement!);

    expect(card.getByText("The submit button does nothing on Safari.")).toBeInTheDocument();
    expect(card.getByLabelText(/issuer note/i)).toBeInTheDocument();
    expect(card.getByRole("button", { name: /^dismissed$/i })).toBeInTheDocument();
  });

  it("clicking a status button calls updateFeedbackStatus", async () => {
    const user = userEvent.setup();
    let captured: unknown = null;
    server.use(
      http.patch("http://localhost/api/v1/platform/feedback/:id/status", async ({ request, params }) => {
        captured = await request.json();
        return HttpResponse.json({ ...REPORTS[0], id: params.id as string, status: "Reviewing" });
      }),
    );
    renderPage();
    await screen.findByText("Broken button");

    const toggle = screen.getByRole("button", { name: /broken button/i, expanded: false });
    await user.click(toggle);
    const card = within(toggle.parentElement!);
    await user.click(card.getByRole("button", { name: /^reviewing$/i }));

    await waitFor(() => expect(captured).toMatchObject({ status: "Reviewing" }));
  });

  it("shows a success toast after a status update", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Broken button");

    const toggle = screen.getByRole("button", { name: /broken button/i, expanded: false });
    await user.click(toggle);
    const card = within(toggle.parentElement!);
    await user.click(card.getByRole("button", { name: /^reviewing$/i }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Updated."));
  });

  it("shows an empty state when there is no feedback", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/feedback", () => HttpResponse.json([])),
    );
    renderPage();

    expect(await screen.findByText(/no feedback yet/i)).toBeInTheDocument();
  });

  it("shows an error state with a retry button", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/feedback", () => HttpResponse.json({ message: "fail" }, { status: 500 })),
    );
    renderPage();

    expect(await screen.findByText(/failed to load feedback/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /retry/i })).toBeInTheDocument();
  });
});
