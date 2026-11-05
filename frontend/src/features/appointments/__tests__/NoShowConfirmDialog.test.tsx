import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { NoShowConfirmDialog } from "@/features/appointments/components/NoShowConfirmDialog";
import type {
  AppointmentResponse, NoShowOutcome, NoShowPreviewResponse, NoShowResultResponse,
} from "@/features/appointments/appointment.types";
import type { StudioResponse } from "@/features/studios/studiosApi";
import { Role } from "@/shared/types/roles";

// E04b (A04): the deposit consequence is shown before the no-show is committed, from the server's own
// decision; confirm waits for it; the safe action has focus; the result shown afterwards is the
// server's, not the projection; and nothing offers an undo.

const STUDIO = {
  id: "s-001", name: "Test Studio", slug: "t", city: "Tirana", latitude: 0, longitude: 0,
  showPlatformBranding: false, allowBrandingRemoval: false, allowApiAccess: false,
  trialExpiresAt: "2030-01-01T00:00:00Z", createdAt: "2024-01-01T00:00:00Z", isActive: true,
  slugLockedAt: null, phoneNumber: null, instagramHandle: null, nipt: null, isSolo: false, isPublished: true,
  timezone: "Europe/Tirane", addressLine1: null, addressLine2: null, postalCode: null,
  countryCode: "PT", currency: "EUR", currencyLocked: false,
} satisfies StudioResponse;

const APPT: AppointmentResponse = {
  id: "appt-1", studioId: "s-001", artistId: "a-001", clientId: "c-001", clientName: "Mira Hoxha",
  date: "2026-11-04T13:00:00Z", endDate: "2026-11-04T15:00:00Z", durationMinutes: 120,
  status: "Confirmed", depositStatus: "Paid", depositAmount: 80,
  notes: null, createdAt: "2026-10-01T00:00:00Z",
};

function preview(outcome: NoShowOutcome, overrides: Partial<NoShowPreviewResponse> = {}): NoShowPreviewResponse {
  return {
    appointmentId: "appt-1", canMarkNoShow: true, message: null, outcome,
    amount: outcome === "NothingCollected" ? 0 : 80, currency: "EUR", isProjection: true, ...overrides,
  };
}

function result(outcome: NoShowOutcome, amount = 80): NoShowResultResponse {
  return { appointment: { ...APPT, status: "NoShow", depositStatus: "Forfeited" }, outcome, amount, currency: "EUR", paymentStatus: "Paid" };
}

const previewReturns = (body: NoShowPreviewResponse) =>
  http.get("http://localhost/api/v1/appointments/:id/no-show-preview", () => HttpResponse.json(body));

let commits = 0;
const server = setupServer(
  http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json(STUDIO)),
  previewReturns(preview("ChargedDepositKept")),
  http.patch("http://localhost/api/v1/appointments/:id/no-show", () => {
    commits += 1;
    return HttpResponse.json(result("ChargedDepositKept"));
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); commits = 0; });
afterAll(() => server.close());

function renderDialog() {
  const store = configureStore({
    reducer: {
      auth: authReducer,
      ui: uiReducer,
      [appointmentsApi.reducerPath]: appointmentsApi.reducer,
      [paymentsApi.reducerPath]: paymentsApi.reducer,
      [studiosApi.reducerPath]: studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(appointmentsApi.middleware).concat(paymentsApi.middleware).concat(studiosApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u-1", email: "a@test.com" }, token: "t", tenantId: "s-001", role: Role.Artist, pendingReferralCode: null, impersonation: null } as any,
      ui: { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null, impersonationScopeError: null, impersonationSessionExpired: false },
    },
  });
  render(
    <Provider store={store}>
      <NoShowConfirmDialog appointment={APPT} open onOpenChange={() => {}} />
    </Provider>,
  );
}

const confirmButton = () => screen.getByRole("button", { name: "Mark no-show" });
const keepButton = () => screen.getByRole("button", { name: "Keep the appointment" });

describe("NoShowConfirmDialog — before committing", () => {
  it("names the client and the session, and focuses the safe action", async () => {
    renderDialog();
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
    expect(screen.getByText("Mark Mira Hoxha as a no-show?")).toBeInTheDocument();
    expect(await screen.findByText("Wed 4 Nov, 14:00 – 16:00")).toBeInTheDocument();
    expect(keepButton()).toHaveFocus();
  });

  it("while the preview loads: says so, and confirm is disabled", () => {
    renderDialog();
    expect(screen.getByText(/checking what would happen to the deposit/i)).toBeInTheDocument();
    expect(confirmButton()).toBeDisabled();
  });

  it("says plainly that it can't be undone, and offers no undo", async () => {
    renderDialog();
    expect(await screen.findByText(/this can't be undone/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /undo/i })).not.toBeInTheDocument();
  });

  it.each([
    ["NothingCollected", /no deposit was paid, so no money moves/i],
    ["ChargedDepositKept", /keeps the .*80.* deposit the client already paid by card/i],
    ["CashDepositKept", /keeps the .*80.* cash deposit it received/i],
    ["HeldDepositCaptured", /80.* held on the client's card will be charged now and kept/i],
    ["DeclaredCashNotCollected", /no cash was received, so there's nothing to keep.*recorded as not collected/i],
  ] as const)("%s: states the server's outcome with the amount, and enables confirm", async (outcome, words) => {
    server.use(previewReturns(preview(outcome)));
    renderDialog();
    expect(await screen.findByText(words)).toBeInTheDocument();
    await waitFor(() => expect(confirmButton()).toBeEnabled());
  });

  it("a failed preview: confirm stays disabled, says who to check with, and can be retried", async () => {
    let calls = 0;
    server.use(http.get("http://localhost/api/v1/appointments/:id/no-show-preview", () => {
      calls += 1;
      return calls === 1 ? HttpResponse.json({ message: "boom" }, { status: 500 }) : HttpResponse.json(preview("ChargedDepositKept"));
    }));
    const user = userEvent.setup();
    renderDialog();
    expect(await screen.findByText(/couldn't check what would happen to the deposit/i)).toBeInTheDocument();
    expect(screen.getByText(/studio owner/i)).toBeInTheDocument();
    expect(confirmButton()).toBeDisabled();
    await user.click(screen.getByRole("button", { name: /try again/i }));
    await waitFor(() => expect(confirmButton()).toBeEnabled());
  });

  it("a session that isn't the artist's own (404): says who can mark it, and confirm stays disabled", async () => {
    server.use(http.get("http://localhost/api/v1/appointments/:id/no-show-preview", () =>
      HttpResponse.json({ message: "not found" }, { status: 404 })));
    renderDialog();
    expect(await screen.findByText(/only the artist assigned to this session, or the studio owner/i)).toBeInTheDocument();
    expect(confirmButton()).toBeDisabled();
  });

  it("an unknown outcome: confirm stays disabled — never a locally worked-out figure", async () => {
    server.use(previewReturns(preview("Unknown")));
    renderDialog();
    expect(await screen.findByText(/can't be worked out/i)).toBeInTheDocument();
    expect(confirmButton()).toBeDisabled();
  });

  it("a closed appointment: the server's refusal, and confirm disabled", async () => {
    server.use(previewReturns(preview("NothingCollected", {
      canMarkNoShow: false, message: "Cannot mark no-show for an appointment with status Completed.",
    })));
    renderDialog();
    expect(await screen.findByText("Cannot mark no-show for an appointment with status Completed.")).toBeInTheDocument();
    expect(confirmButton()).toBeDisabled();
  });
});

describe("NoShowConfirmDialog — after committing", () => {
  it("shows what the server says it did, not the projection", async () => {
    // Previewed as a declared cash deposit, but the studio confirmed the cash in between: the commit
    // reports the deposit as kept, and that is what is shown.
    server.use(
      previewReturns(preview("DeclaredCashNotCollected")),
      http.patch("http://localhost/api/v1/appointments/:id/no-show", () => {
        commits += 1;
        return HttpResponse.json(result("CashDepositKept"));
      }),
    );
    const user = userEvent.setup();
    renderDialog();
    await waitFor(() => expect(confirmButton()).toBeEnabled());
    await user.click(confirmButton());

    expect(await screen.findByText(/marked as a no-show\. the studio kept the .*80.* deposit/i)).toBeInTheDocument();
    expect(screen.queryByText(/no cash was received/i)).not.toBeInTheDocument();
    expect(commits).toBe(1);
    expect(screen.queryByRole("button", { name: "Mark no-show" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Close" })).toBeInTheDocument();
  });

  it("a failed commit says so and doesn't claim anything happened", async () => {
    server.use(http.patch("http://localhost/api/v1/appointments/:id/no-show", () =>
      HttpResponse.json({ message: "boom" }, { status: 500 })));
    const user = userEvent.setup();
    renderDialog();
    await waitFor(() => expect(confirmButton()).toBeEnabled());
    await user.click(confirmButton());
    expect(await screen.findByText(/couldn't be confirmed\. check the appointment's status/i)).toBeInTheDocument();
    expect(screen.queryByText(/marked as a no-show/i)).not.toBeInTheDocument();
  });
});
