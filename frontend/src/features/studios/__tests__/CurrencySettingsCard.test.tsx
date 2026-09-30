import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { Toaster } from "sonner";

import authReducer from "@/features/auth/authSlice";
import { studiosApi, type StudioResponse } from "@/features/studios/studiosApi";
import { CurrencySettingsCard } from "@/features/studios/components/CurrencySettingsCard";

// ── Fixtures ─────────────────────────────────────────────────────────────────

const UNLOCKED_STUDIO: StudioResponse = {
  id:                   "studio-001",
  name:                 "Ink & Soul Studio",
  slug:                 "ink-soul-studio",
  city:                 "Lisbon",
  latitude:             38.7169,
  longitude:            -9.1395,
  showPlatformBranding: true,
  allowBrandingRemoval: false,
  allowApiAccess:       false,
  trialExpiresAt:       "2099-01-01T00:00:00Z",
  createdAt:            "2025-01-01T00:00:00Z",
  isActive:             true,
  slugLockedAt:         null,
  phoneNumber:          null,
  instagramHandle:      null,
  nipt:                 null,
  isSolo:               false,
  isPublished:          true,
  timezone:             "Europe/Tirane",
  addressLine1:         null,
  addressLine2:         null,
  postalCode:           null,
  countryCode:          "AL",
  currency:             "EUR",
  currencyLocked:       false,
};

const LOCKED_STUDIO: StudioResponse = { ...UNLOCKED_STUDIO, currency: "ALL", currencyLocked: true };

// ── MSW server ────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json(UNLOCKED_STUDIO)),
  http.put("http://localhost/api/v1/studios/me/currency", () => HttpResponse.json({ ...UNLOCKED_STUDIO, currency: "ALL" })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ───────────────────────────────────────────────────────────────────

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
      <MemoryRouter>
        <Toaster />
        <CurrencySettingsCard />
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("CurrencySettingsCard", () => {
  it("shows a loading skeleton before studio data arrives", () => {
    renderCard();
    expect(screen.getByText("Currency")).toBeInTheDocument();
    // The unlocked/locked content isn't rendered yet while the query is in flight.
    expect(screen.queryByRole("button", { name: /^save$/i })).not.toBeInTheDocument();
  });

  it("shows an error state with a retry button when the fetch fails", async () => {
    server.use(http.get("http://localhost/api/v1/studios/me", () => HttpResponse.error()));
    renderCard();
    expect(await screen.findByText(/couldn't load currency settings/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /retry/i })).toBeInTheDocument();
  });

  it("retry re-fetches and renders the unlocked picker on success", async () => {
    let calls = 0;
    server.use(
      http.get("http://localhost/api/v1/studios/me", () => {
        calls++;
        return calls === 1 ? HttpResponse.error() : HttpResponse.json(UNLOCKED_STUDIO);
      }),
    );
    const user = userEvent.setup();
    renderCard();
    await screen.findByRole("button", { name: /retry/i });
    await user.click(screen.getByRole("button", { name: /retry/i }));
    expect(await screen.findByRole("button", { name: /^save$/i })).toBeInTheDocument();
  });

  it("shows the currency picker and a disabled Save button when unchanged", async () => {
    renderCard();
    await screen.findByText("€ — EUR");
    expect(screen.getByRole("button", { name: /^save$/i })).toBeDisabled();
  });

  it("selecting a new currency and confirming calls the mutation with the new currency", async () => {
    let capturedBody: unknown;
    server.use(
      http.put("http://localhost/api/v1/studios/me/currency", async ({ request }) => {
        capturedBody = await request.json();
        return HttpResponse.json({ ...UNLOCKED_STUDIO, currency: "ALL" });
      }),
    );
    const user = userEvent.setup();
    renderCard();
    await screen.findByText("€ — EUR");

    await user.click(screen.getByRole("combobox"));
    await user.click(await screen.findByRole("option", { name: /Albanian Lek \(ALL\)/i }));
    expect(screen.getByRole("button", { name: /^save$/i })).not.toBeDisabled();

    await user.click(screen.getByRole("button", { name: /^save$/i }));
    expect(await screen.findByText(/change currency to all\?/i)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /change currency/i }));

    await waitFor(() => expect(capturedBody).toEqual({ currency: "ALL" }));
    expect(await screen.findByText(/currency updated/i)).toBeInTheDocument();
  });

  it("cancelling the confirmation dialog does not call the mutation", async () => {
    const putSpy = vi.fn();
    server.use(
      http.put("http://localhost/api/v1/studios/me/currency", async ({ request }) => {
        putSpy(await request.json());
        return HttpResponse.json({ ...UNLOCKED_STUDIO, currency: "ALL" });
      }),
    );
    const user = userEvent.setup();
    renderCard();
    await screen.findByText("€ — EUR");

    await user.click(screen.getByRole("combobox"));
    await user.click(await screen.findByRole("option", { name: /Albanian Lek \(ALL\)/i }));
    await user.click(screen.getByRole("button", { name: /^save$/i }));
    await screen.findByText(/change currency to all\?/i);

    await user.click(screen.getByRole("button", { name: /^cancel$/i }));
    expect(putSpy).not.toHaveBeenCalled();
  });

  it("shows the server error message when the currency change fails", async () => {
    server.use(
      http.put("http://localhost/api/v1/studios/me/currency", () =>
        HttpResponse.json({ message: "Card provider does not support this currency." }, { status: 409 }),
      ),
    );
    const user = userEvent.setup();
    renderCard();
    await screen.findByText("€ — EUR");

    await user.click(screen.getByRole("combobox"));
    await user.click(await screen.findByRole("option", { name: /Albanian Lek \(ALL\)/i }));
    await user.click(screen.getByRole("button", { name: /^save$/i }));
    await screen.findByText(/change currency to all\?/i);
    await user.click(screen.getByRole("button", { name: /change currency/i }));

    expect(await screen.findByText(/card provider does not support this currency/i)).toBeInTheDocument();
  });

  it("shows a locked, read-only currency with a support link and no picker", async () => {
    server.use(http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json(LOCKED_STUDIO)));
    renderCard();

    expect(await screen.findByText("Albanian Lek (ALL)")).toBeInTheDocument();
    expect(screen.getByText(/locked because payments have been recorded/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /contact support/i })).toBeInTheDocument();
    expect(screen.queryByRole("combobox")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^save$/i })).not.toBeInTheDocument();
  });
});
