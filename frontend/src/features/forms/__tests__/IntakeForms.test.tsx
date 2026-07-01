import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { intakeFormsApi } from "@/features/forms/intakeFormsApi";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";

import { SubmitIntakeFormPage } from "@/features/forms/components/SubmitIntakeFormPage";
import { IntakeFormListPage } from "@/features/forms/components/IntakeFormListPage";
import { IntakeFormDetailPage } from "@/features/forms/components/IntakeFormDetailPage";

import type { IntakeFormResponse } from "@/features/forms/form.types";
import type { AppointmentResponse } from "@/features/appointments/appointment.types";
import { Role } from "@/shared/types/roles";

// ── Seed data ──────────────────────────────────────────────────────────────────

const FUTURE = new Date(Date.now() + 7 * 86_400_000).toISOString();
const FUTURE_END = new Date(Date.now() + 7 * 86_400_000 + 3_600_000).toISOString();

const APPOINTMENT: AppointmentResponse = {
  id: "appt-001", studioId: "s-001",
  artistId: "a-001", clientId: "c-001",
  date: FUTURE, endDate: FUTURE_END,
  durationMinutes: 60, status: "Pending",
  depositStatus: "Pending", depositAmount: 0,
  notes: null, createdAt: "2024-01-01T00:00:00Z",
};

const SUBMITTED_FORM: IntakeFormResponse = {
  id: "if-001", studioId: "s-001", clientId: "c-001",
  appointmentId: "appt-001",
  formData: JSON.stringify({
    fullName: "Marco Cliente",
    dateOfBirth: "1990-05-12",
    hasAllergies: true,
    allergyDetails: "Latex",
    acknowledgesAftercare: true,
  }),
  fileUrl: "https://r2.example.com/form.pdf",
  submittedAt: "2024-02-01T10:00:00Z",
  createdAt: "2024-02-01T09:55:00Z",
};

const PLAIN_TEXT_FORM: IntakeFormResponse = {
  id: "if-002", studioId: "s-001", clientId: "c-002",
  appointmentId: null,
  formData: "No known allergies, in good health.",
  fileUrl: null,
  submittedAt: null,
  createdAt: "2024-02-02T09:00:00Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/appointments", () => HttpResponse.json([APPOINTMENT])),
  http.post("http://localhost/api/v1/intake-forms", () => HttpResponse.json(SUBMITTED_FORM, { status: 201 })),
  http.get("http://localhost/api/v1/intake-forms", () => HttpResponse.json([SUBMITTED_FORM, PLAIN_TEXT_FORM])),
  http.get("http://localhost/api/v1/intake-forms/:id", ({ params }) =>
    params.id === SUBMITTED_FORM.id
      ? HttpResponse.json(SUBMITTED_FORM)
      : HttpResponse.json(PLAIN_TEXT_FORM),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore(role: Role = Role.Client) {
  return configureStore({
    reducer: {
      auth: authReducer,
      ui: uiReducer,
      [intakeFormsApi.reducerPath]: intakeFormsApi.reducer,
      [appointmentsApi.reducerPath]: appointmentsApi.reducer,
    },
    middleware: (gd) =>
      gd().concat(intakeFormsApi.middleware).concat(appointmentsApi.middleware),
    preloadedState: {
      auth: {
        user: { id: "u-001", email: "test@test.com" },
        token: "fake-token",
        tenantId: "s-001",
        role,
        pendingReferralCode: null,
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any,
      ui: { readOnlyError: null, sessionExpired: false, studioSuspended: false },
    },
  });
}

function renderWithRoute(node: React.ReactElement, initialEntry = "/", role: Role = Role.Client) {
  return render(
    <Provider store={makeStore(role)}>
      <MemoryRouter initialEntries={[initialEntry]}>{node}</MemoryRouter>
    </Provider>,
  );
}

function renderDetailPage(id: string, role: Role = Role.Client) {
  return render(
    <Provider store={makeStore(role)}>
      <MemoryRouter initialEntries={[`/forms/intake/${id}`]}>
        <Routes>
          <Route path="/forms/intake/:id" element={<IntakeFormDetailPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── SubmitIntakeFormPage ───────────────────────────────────────────────────────

describe("SubmitIntakeFormPage", () => {
  it("renders the page heading and form fields", () => {
    renderWithRoute(<SubmitIntakeFormPage />);
    expect(screen.getByText("Intake Form")).toBeInTheDocument();
    expect(screen.getByLabelText(/medical history/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/appointment/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/attachment url/i)).toBeInTheDocument();
  });

  it("shows a validation error when formData is too short", async () => {
    const user = userEvent.setup();
    renderWithRoute(<SubmitIntakeFormPage />);
    await user.type(screen.getByLabelText(/medical history/i), "short");
    await user.click(screen.getByRole("button", { name: /submit intake form/i }));
    expect(await screen.findByText(/at least 10 characters/i)).toBeInTheDocument();
  });

  it("shows a validation error for an invalid attachment URL", async () => {
    const user = userEvent.setup();
    renderWithRoute(<SubmitIntakeFormPage />);
    await user.type(screen.getByLabelText(/medical history/i), "No allergies of any kind.");
    await user.type(screen.getByLabelText(/attachment url/i), "not-a-url");
    await user.click(screen.getByRole("button", { name: /submit intake form/i }));
    expect(await screen.findByText(/must be a valid url/i)).toBeInTheDocument();
  });

  it("lists appointments in the appointment selector", async () => {
    renderWithRoute(<SubmitIntakeFormPage />);
    expect(await screen.findByText(new Date(FUTURE).toLocaleDateString("en-GB", {
      day: "numeric", month: "short", year: "numeric",
    }))).toBeInTheDocument();
  });

  it("shows a confirmation screen after successful submission", async () => {
    const user = userEvent.setup();
    renderWithRoute(<SubmitIntakeFormPage />);
    await user.type(screen.getByLabelText(/medical history/i), "No allergies of any kind.");
    await user.click(screen.getByRole("button", { name: /submit intake form/i }));
    expect(await screen.findByText("Intake form submitted!")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /submit another/i })).toBeInTheDocument();
  });

  it("'Submit another' returns to the empty form", async () => {
    const user = userEvent.setup();
    renderWithRoute(<SubmitIntakeFormPage />);
    await user.type(screen.getByLabelText(/medical history/i), "No allergies of any kind.");
    await user.click(screen.getByRole("button", { name: /submit intake form/i }));
    await screen.findByText("Intake form submitted!");
    await user.click(screen.getByRole("button", { name: /submit another/i }));
    expect(screen.getByRole("button", { name: /submit intake form/i })).toBeInTheDocument();
  });

  it("shows an error message when submission fails", async () => {
    server.use(
      http.post("http://localhost/api/v1/intake-forms", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderWithRoute(<SubmitIntakeFormPage />);
    await user.type(screen.getByLabelText(/medical history/i), "No allergies of any kind.");
    await user.click(screen.getByRole("button", { name: /submit intake form/i }));
    expect(await screen.findByText(/failed to submit/i)).toBeInTheDocument();
  });
});

// ── IntakeFormListPage ─────────────────────────────────────────────────────────

describe("IntakeFormListPage", () => {
  it("shows loading skeletons while fetching", () => {
    renderWithRoute(<IntakeFormListPage />, "/forms/intake");
    expect(document.querySelectorAll(".animate-pulse").length).toBe(5);
    expect(screen.getByText("Intake Forms")).toBeInTheDocument();
  });

  it("shows an error message when the fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/intake-forms", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    renderWithRoute(<IntakeFormListPage />, "/forms/intake");
    expect(await screen.findByText(/failed to load intake forms/i)).toBeInTheDocument();
  });

  it("shows rich empty state when there are no forms and no filters", async () => {
    server.use(
      http.get("http://localhost/api/v1/intake-forms", () => HttpResponse.json([])),
    );
    renderWithRoute(<IntakeFormListPage />, "/forms/intake");
    expect(await screen.findByText(/no intake forms yet/i)).toBeInTheDocument();
  });

  it("renders the parsed full name as the row headline", async () => {
    renderWithRoute(<IntakeFormListPage />, "/forms/intake");
    expect(await screen.findByText("Marco Cliente")).toBeInTheDocument();
  });

  it("renders plain text forms with a truncated headline", async () => {
    renderWithRoute(<IntakeFormListPage />, "/forms/intake");
    expect(await screen.findByText("No known allergies, in good health.")).toBeInTheDocument();
  });

  it("shows the 'Submitted' badge for a submitted form and 'Draft' otherwise", async () => {
    renderWithRoute(<IntakeFormListPage />, "/forms/intake");
    await screen.findByText("Marco Cliente");
    expect(screen.getByText("Submitted")).toBeInTheDocument();
    expect(screen.getByText("Draft")).toBeInTheDocument();
  });

  it("shows the form count in the header", async () => {
    renderWithRoute(<IntakeFormListPage />, "/forms/intake");
    expect(await screen.findByText("2 forms")).toBeInTheDocument();
  });

  it("shows a filter banner when clientId is present in the query string", async () => {
    renderWithRoute(<IntakeFormListPage />, "/forms/intake?clientId=c-001");
    expect(await screen.findByText("c-001")).toBeInTheDocument();
  });
});

// ── IntakeFormDetailPage ───────────────────────────────────────────────────────

describe("IntakeFormDetailPage", () => {
  it("shows a loading skeleton then renders structured medical history", async () => {
    renderDetailPage(SUBMITTED_FORM.id);
    expect(screen.getByLabelText(/loading intake form/i)).toBeInTheDocument();
    expect(await screen.findByText("Marco Cliente")).toBeInTheDocument();
  });

  it("renders health flags and allergy details for structured data", async () => {
    renderDetailPage(SUBMITTED_FORM.id);
    await screen.findByText("Marco Cliente");
    expect(screen.getByText("Allergies")).toBeInTheDocument();
    expect(screen.getByText("Latex")).toBeInTheDocument();
    expect(screen.getByText("Acknowledges aftercare instructions")).toBeInTheDocument();
  });

  it("renders an attachment link when fileUrl is present", async () => {
    renderDetailPage(SUBMITTED_FORM.id);
    const link = await screen.findByRole("link", { name: /view file/i });
    expect(link).toHaveAttribute("href", SUBMITTED_FORM.fileUrl);
  });

  it("falls back to plain text rendering for non-JSON formData", async () => {
    server.use(
      http.get("http://localhost/api/v1/intake-forms/:id", () => HttpResponse.json(PLAIN_TEXT_FORM)),
    );
    renderDetailPage(PLAIN_TEXT_FORM.id);
    expect(await screen.findByText("No known allergies, in good health.")).toBeInTheDocument();
    expect(screen.getByText("Draft")).toBeInTheDocument();
  });

  it("shows an error message when the fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/intake-forms/:id", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    renderDetailPage(SUBMITTED_FORM.id);
    expect(await screen.findByText(/failed to load intake form/i)).toBeInTheDocument();
  });
});
