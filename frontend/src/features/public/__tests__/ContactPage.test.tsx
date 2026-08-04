import { describe, it, expect, beforeAll, afterEach, afterAll, vi } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { contactApi } from "@/features/public/contactApi";
import { ContactPage } from "@/features/public/components/ContactPage";

// Toasts are irrelevant to these assertions; stub to keep them quiet.
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

interface ContactBody {
  name: string;
  email: string;
  message: string;
}
let lastBody: ContactBody | null = null;

const server = setupServer(
  http.post("*/api/v1/contact", async ({ request }) => {
    lastBody = (await request.json()) as ContactBody;
    return new HttpResponse(null, { status: 202 });
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  server.resetHandlers();
  cleanup();
  lastBody = null;
});
afterAll(() => server.close());

function renderPage() {
  const store = configureStore({
    reducer: { auth: authReducer, [contactApi.reducerPath]: contactApi.reducer },
    middleware: (getDefault) => getDefault().concat(contactApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: null, token: null, tenantId: null, role: null, pendingReferralCode: null } as any,
    },
  });
  render(
    <Provider store={store}>
      <MemoryRouter>
        <ContactPage />
      </MemoryRouter>
    </Provider>,
  );
}

describe("ContactPage", () => {
  it("renders name, email, and message fields", () => {
    renderPage();
    expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/email/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/message/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /send message/i })).toBeInTheDocument();
  });

  it("shows client-side validation errors on empty submit and does not call the API", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole("button", { name: /send message/i }));

    expect(await screen.findByText(/please enter your name/i)).toBeInTheDocument();
    expect(screen.getByText(/please enter your email/i)).toBeInTheDocument();
    expect(screen.getByText(/please enter a message/i)).toBeInTheDocument();
    expect(lastBody).toBeNull();
  });

  it("submits the form and resets on success", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/name/i), "Ana Costa");
    await user.type(screen.getByLabelText(/email/i), "ana@example.com");
    await user.type(screen.getByLabelText(/message/i), "I have a question");
    await user.click(screen.getByRole("button", { name: /send message/i }));

    await waitFor(() => expect(lastBody).not.toBeNull());
    expect(lastBody).toEqual({
      name: "Ana Costa",
      email: "ana@example.com",
      message: "I have a question",
    });
    await waitFor(() => expect(screen.getByLabelText(/name/i)).toHaveValue(""));
  });
});
