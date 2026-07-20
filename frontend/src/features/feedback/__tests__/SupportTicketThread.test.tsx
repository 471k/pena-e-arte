import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { feedbackApi } from "../feedbackApi";
import { SupportTicketThread } from "../components/SupportTicketThread";
import type { FeedbackReportResponse, FeedbackMessageResponse } from "../feedback.types";

const REPORT: FeedbackReportResponse = {
  id: "fb-1", type: "SupportRequest", title: "Billing question", body: "Where do I see my invoices?",
  status: "Open", studioName: "Ink Soul", submitterRole: "owner",
  issuerNote: null, createdAt: "2026-07-21T00:00:00.000Z", resolvedAt: null,
};

const MESSAGES: FeedbackMessageResponse[] = [
  {
    id: "msg-1", feedbackReportId: "fb-1", authorUserId: "u1", authorRole: "owner",
    body: "Any update?", createdAt: "2026-07-21T01:00:00.000Z",
  },
  {
    id: "msg-2", feedbackReportId: "fb-1", authorUserId: "u9", authorRole: "issuer",
    body: "Looking into it now.", createdAt: "2026-07-21T02:00:00.000Z",
  },
];

const server = setupServer(
  http.get("http://localhost/api/v1/feedback/fb-1/messages", () => HttpResponse.json(MESSAGES)),
  http.post("http://localhost/api/v1/feedback/fb-1/messages", async ({ request }) => {
    const body = await request.json() as { body: string };
    return HttpResponse.json({
      id: "msg-3", feedbackReportId: "fb-1", authorUserId: "u1", authorRole: "owner",
      body: body.body, createdAt: "2026-07-21T03:00:00.000Z",
    });
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

function renderThread(canReply = true) {
  const store = configureStore({
    reducer: { auth: authReducer, [feedbackApi.reducerPath]: feedbackApi.reducer },
    middleware: (gd) => gd().concat(feedbackApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "owner@test.com" }, token: "fake", tenantId: "t1", role: "owner" } as any,
    },
  });
  render(
    <Provider store={store}>
      <SupportTicketThread report={REPORT} canReply={canReply} />
    </Provider>,
  );
}

describe("SupportTicketThread", () => {
  it("renders the ticket title, status badge, and original body", () => {
    renderThread();
    expect(screen.getByText("Billing question")).toBeInTheDocument();
    expect(screen.getByText("Open")).toBeInTheDocument();
    expect(screen.getByText("Where do I see my invoices?")).toBeInTheDocument();
  });

  it("renders every message once loaded", async () => {
    renderThread();
    expect(await screen.findByText("Any update?")).toBeInTheDocument();
    expect(screen.getByText("Looking into it now.")).toBeInTheDocument();
  });

  it("shows the reply box when canReply is true", () => {
    renderThread(true);
    expect(screen.getByPlaceholderText(/type a reply/i)).toBeInTheDocument();
  });

  it("hides the reply box when canReply is false", () => {
    renderThread(false);
    expect(screen.queryByPlaceholderText(/type a reply/i)).not.toBeInTheDocument();
  });

  it("sending a reply posts the message and clears the input", async () => {
    const user = userEvent.setup();
    renderThread();
    await screen.findByText("Any update?");

    await user.type(screen.getByPlaceholderText(/type a reply/i), "Thanks, appreciated.");
    await user.click(screen.getByRole("button", { name: /send reply/i }));

    await waitFor(() => expect(screen.getByPlaceholderText(/type a reply/i)).toHaveValue(""));
  });

  it("the send button is disabled when the reply box is empty", () => {
    renderThread();
    expect(screen.getByRole("button", { name: /send reply/i })).toBeDisabled();
  });
});
