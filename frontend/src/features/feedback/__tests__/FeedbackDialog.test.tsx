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
import { FeedbackDialog } from "@/features/feedback/components/FeedbackDialog";
import type { FeedbackReportResponse } from "@/features/feedback/feedback.types";

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));

const CREATED_REPORT: FeedbackReportResponse = {
  id:            "fb-0001",
  type:          "BugReport",
  title:         "Broken button",
  body:          "The submit button does nothing on Safari.",
  status:        "Open",
  studioName:    "Test Studio",
  submitterRole: "artist",
  issuerNote:    null,
  createdAt:     "2026-07-01T00:00:00.000Z",
  resolvedAt:    null,
};

const server = setupServer(
  http.post("http://localhost/api/v1/feedback", () => HttpResponse.json(CREATED_REPORT), ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth:                       authReducer,
      [feedbackApi.reducerPath]:  feedbackApi.reducer,
    },
    middleware: (gd) => gd().concat(feedbackApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "artist@test.com" }, token: "fake", tenantId: "t1", role: "artist" } as any,
    },
  });
}

function renderDialog(onOpenChange = vi.fn()) {
  const store = makeStore();
  render(
    <Provider store={store}>
      <FeedbackDialog open onOpenChange={onOpenChange} />
    </Provider>,
  );
  return { onOpenChange };
}

describe("FeedbackDialog", () => {
  it("renders with type selector, title input, body textarea, cancel + submit buttons", () => {
    renderDialog();

    expect(screen.getByLabelText(/type/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/title/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /cancel/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /send feedback/i })).toBeInTheDocument();
  });

  it("type selector defaults to Bug Report", () => {
    renderDialog();
    expect(screen.getByLabelText(/type/i)).toHaveTextContent(/bug report/i);
  });

  it("empty title shows validation error on submit", async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/description/i), "A description with enough characters.");
    await user.click(screen.getByRole("button", { name: /send feedback/i }));

    expect(await screen.findByText(/title is required/i)).toBeInTheDocument();
  });

  it("body under 10 chars shows validation error on submit", async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/title/i), "Short body test");
    await user.type(screen.getByLabelText(/description/i), "short");
    await user.click(screen.getByRole("button", { name: /send feedback/i }));

    expect(await screen.findByText(/at least 10 characters/i)).toBeInTheDocument();
  });

  it("valid form submission calls submitFeedback mutation", async () => {
    const user = userEvent.setup();
    let captured: unknown = null;
    server.use(
      http.post("http://localhost/api/v1/feedback", async ({ request }) => {
        captured = await request.json();
        return HttpResponse.json(CREATED_REPORT);
      }),
    );
    renderDialog();

    await user.type(screen.getByLabelText(/title/i), "Broken button");
    await user.type(screen.getByLabelText(/description/i), "The submit button does nothing on Safari.");
    await user.click(screen.getByRole("button", { name: /send feedback/i }));

    await waitFor(() => expect(captured).toMatchObject({ title: "Broken button" }), { timeout: 10000 });
  }, 15000);

  it("shows a spinner on the submit button while loading", async () => {
    const user = userEvent.setup();
    // Never resolves — keeps the mutation pending for the life of the test,
    // so the loading assertion below is not a race against a timed response.
    server.use(
      http.post("http://localhost/api/v1/feedback", () => new Promise(() => {})),
    );
    renderDialog();

    await user.type(screen.getByLabelText(/title/i), "Broken button");
    await user.type(screen.getByLabelText(/description/i), "The submit button does nothing on Safari.");
    await user.click(screen.getByRole("button", { name: /send feedback/i }));

    await waitFor(() => expect(document.querySelector(".animate-spin")).toBeInTheDocument(), { timeout: 10000 });
  }, 15000);

  it("shows the thank-you confirmation view on success", async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/title/i), "Broken button");
    await user.type(screen.getByLabelText(/description/i), "The submit button does nothing on Safari.");
    await user.click(screen.getByRole("button", { name: /send feedback/i }));

    expect(await screen.findByText(/thank you for your feedback/i)).toBeInTheDocument();
  });

  it("close button after success calls onOpenChange(false)", async () => {
    const user = userEvent.setup();
    const { onOpenChange } = renderDialog();

    await user.type(screen.getByLabelText(/title/i), "Broken button");
    await user.type(screen.getByLabelText(/description/i), "The submit button does nothing on Safari.");
    await user.click(screen.getByRole("button", { name: /send feedback/i }));

    const thankYou = await screen.findByText(/thank you for your feedback/i);
    await user.click(within(thankYou.closest("div")!).getByRole("button", { name: /^close$/i }));

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("closing resets the form back to default state", async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/title/i), "Some draft title");
    await user.click(screen.getByRole("button", { name: /cancel/i }));

    expect(screen.getByLabelText(/title/i)).toHaveValue("");
  });

  it("shows an error toast when the mutation fails", async () => {
    const user = userEvent.setup();
    server.use(
      http.post("http://localhost/api/v1/feedback", () => HttpResponse.json({ message: "fail" }, { status: 500 })),
    );
    renderDialog();

    await user.type(screen.getByLabelText(/title/i), "Broken button");
    await user.type(screen.getByLabelText(/description/i), "The submit button does nothing on Safari.");
    await user.click(screen.getByRole("button", { name: /send feedback/i }));

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
  });
});
