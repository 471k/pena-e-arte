import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { toast } from "sonner";
import { MemoryRouter, useLocation } from "react-router-dom";

import authReducer from "@/features/auth/authSlice";
import { feedbackApi } from "@/features/feedback/feedbackApi";
import { FeedbackInboxPage } from "@/features/feedback/components/FeedbackInboxPage";
import type { FeedbackReportResponse } from "@/features/feedback/feedback.types";

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));

const REPORTS: FeedbackReportResponse[] = [
  {
    id: "fb-1", type: "BugReport", title: "Broken button", body: "The submit button does nothing on Safari.",
    status: "Open", studioName: "Studio A", submitterRole: "artist",
    adminNote: null, createdAt: "2026-07-01T00:00:00.000Z", resolvedAt: null,
  },
  {
    id: "fb-3", type: "SupportRequest", title: "Cannot export clients", body: "Export gives an empty file.",
    status: "Reviewing", studioName: "Studio C", submitterRole: "owner",
    adminNote: null, createdAt: "2026-06-20T00:00:00.000Z", resolvedAt: null,
  },
  {
    id: "fb-4", type: "SupportRequest", title: "Login loop fixed", body: "Was looping, now fine.",
    status: "Resolved", studioName: "Studio D", submitterRole: "artist",
    adminNote: null, createdAt: "2026-06-25T00:00:00.000Z", resolvedAt: "2026-06-26T00:00:00.000Z",
  },
  {
    id: "fb-2", type: "FeatureRequest", title: "Add dark mode", body: "It would be great to have a dark theme.",
    status: "Resolved", studioName: "Studio B", submitterRole: "owner",
    adminNote: "Shipped in v2", createdAt: "2026-06-28T00:00:00.000Z", resolvedAt: "2026-06-29T00:00:00.000Z",
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
  http.get("http://localhost/api/v1/feedback/:id/messages", () => HttpResponse.json([])),
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
      auth: { user: { id: "u1", email: "admin@test.com" }, token: "fake", tenantId: null, role: "admin" } as any,
    },
  });
}

function SearchProbe() {
  return <output data-testid="location-search">{useLocation().search}</output>;
}

function renderPage(path = "/platform/feedback") {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[path]}>
        <FeedbackInboxPage />
        <SearchProbe />
      </MemoryRouter>
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

  it("opens on support triage: support requests and bug reports that need action, oldest first", async () => {
    renderPage();

    expect(await screen.findByText("Broken button")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Support triage" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("button", { name: "Needs action" })).toHaveAttribute("aria-pressed", "true");
    // Product feedback isn't mixed in, and a resolved support request doesn't need action.
    expect(screen.queryByText("Add dark mode")).not.toBeInTheDocument();
    expect(screen.queryByText("Login loop fixed")).not.toBeInTheDocument();
    // The Reviewing request from 20 June comes before the Open bug from 1 July.
    const titles = screen.getAllByRole("button", { expanded: false }).map((b) => b.textContent ?? "");
    expect(titles.findIndex((t) => t.includes("Cannot export clients")))
      .toBeLessThan(titles.findIndex((t) => t.includes("Broken button")));
  });

  it("product feedback is its own view, newest first, with every status, and the view is in the URL", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Broken button");

    await user.click(screen.getByRole("tab", { name: "Product feedback" }));

    expect(await screen.findByText("Add dark mode")).toBeInTheDocument();
    expect(screen.queryByText("Broken button")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Needs action" })).not.toBeInTheDocument();
    expect(screen.getByTestId("location-search")).toHaveTextContent("?view=feedback");
  });

  it("a resolved support request is one click away in triage, under its status", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Broken button");

    await user.click(screen.getByRole("button", { name: /^resolved$/i }));

    expect(await screen.findByText("Login loop fixed")).toBeInTheDocument();
  });

  it("shows the Bug Report badge for a BugReport entry", async () => {
    renderPage();

    expect(await screen.findByText("Bug Report")).toBeInTheDocument();
  });

  it("shows the correct status badge per entry", async () => {
    renderPage("/platform/feedback?view=feedback&status=all");

    // Scoped to <span> — the status *filter chip* bar also renders a "Resolved"
    // button, so an unscoped query is ambiguous once both are on screen.
    expect(await screen.findByText("Resolved", { selector: "span" })).toBeInTheDocument();
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
    expect(card.getByLabelText(/admin note/i)).toBeInTheDocument();
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

    expect(await screen.findByText(/nothing needs action/i)).toBeInTheDocument();
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
