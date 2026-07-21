import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { feedbackApi } from "../feedbackApi";
import { SupportRequestForm } from "../components/SupportRequestForm";

const server = setupServer(
  http.post("http://localhost/api/v1/feedback", async ({ request }) => {
    const body = await request.json() as Record<string, unknown>;
    return HttpResponse.json({
      id: "fb-new", type: body.type, title: body.title, body: body.body,
      status: "Open", studioName: "Ink Soul", submitterRole: "client",
      issuerNote: null, createdAt: "2026-07-21T00:00:00.000Z", resolvedAt: null,
    });
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

function renderForm() {
  const store = configureStore({
    reducer: { auth: authReducer, [feedbackApi.reducerPath]: feedbackApi.reducer },
    middleware: (gd) => gd().concat(feedbackApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "client@test.com" }, token: "fake", tenantId: "t1", role: "client" } as any,
    },
  });
  render(
    <Provider store={store}>
      <SupportRequestForm />
    </Provider>,
  );
}

describe("SupportRequestForm", () => {
  it("renders subject and message fields", () => {
    renderForm();
    expect(screen.getByLabelText(/subject/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/message/i)).toBeInTheDocument();
  });

  it("shows a validation error for a message under 10 characters", async () => {
    const user = userEvent.setup();
    renderForm();

    await user.type(screen.getByLabelText(/subject/i), "Need help");
    await user.type(screen.getByLabelText(/message/i), "short");
    await user.click(screen.getByRole("button", { name: /send message/i }));

    expect(await screen.findByText(/at least 10 characters/i)).toBeInTheDocument();
  });

  it("submits with type SupportRequest and clears the form on success", async () => {
    const user = userEvent.setup();
    let captured: unknown = null;
    server.use(
      http.post("http://localhost/api/v1/feedback", async ({ request }) => {
        captured = await request.json();
        return HttpResponse.json({
          id: "fb-new", type: "SupportRequest", title: "Need help", body: "I can't find the billing page.",
          status: "Open", studioName: "Ink Soul", submitterRole: "client",
          issuerNote: null, createdAt: "2026-07-21T00:00:00.000Z", resolvedAt: null,
        });
      }),
    );
    renderForm();

    await user.type(screen.getByLabelText(/subject/i), "Need help");
    await user.type(screen.getByLabelText(/message/i), "I can't find the billing page.");
    await user.click(screen.getByRole("button", { name: /send message/i }));

    await waitFor(() => expect(captured).toMatchObject({ type: "SupportRequest" }));
    await waitFor(() => expect(screen.getByLabelText(/subject/i)).toHaveValue(""));
  });
});
