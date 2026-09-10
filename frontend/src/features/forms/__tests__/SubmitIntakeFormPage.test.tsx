import { describe, it, expect, beforeAll, beforeEach, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { consentFormsApi } from "@/features/forms/consentFormsApi";
import { intakeFormsApi } from "@/features/forms/intakeFormsApi";
import { SubmitIntakeFormPage } from "@/features/forms/components/SubmitIntakeFormPage";
import type { IntakeFormResponse } from "@/features/forms/form.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const NO_CONSENT_TEMPLATE = { id: null, kind: "IntakeFormConsent", version: "", bodyText: "" };

const SUBMITTED_FORM: IntakeFormResponse = {
  id: "if-1", studioId: "s-001", clientId: "u1", appointmentId: null,
  formData: "{}", fileUrl: null, submittedAt: "2024-06-01T10:00:00Z", createdAt: "2024-06-01T10:00:00Z",
};

const FULL_TEMPLATE = {
  id: "tpl-1",
  studioId: "s-001",
  isActive: true,
  createdAt: "2024-01-01T00:00:00Z",
  updatedAt: "2024-01-01T00:00:00Z",
  fieldSchemaJson: JSON.stringify([
    { label: "Allergies",  type: "Text",     required: true },
    { label: "Notes",      type: "Textarea", required: false },
    { label: "Skin type",  type: "Select",   required: true, options: ["Oily", "Dry"] },
    { label: "Confirm ok", type: "Checkbox", required: true },
    { label: "DOB",        type: "Date",     required: false },
  ]),
};

// ── MSW server ─────────────────────────────────────────────────────────────────

let capturedBody: Record<string, unknown> | undefined;

const server = setupServer(
  http.get("http://localhost/api/v1/appointments/mine", () => HttpResponse.json([])),
  http.get("http://localhost/api/v1/consent-forms/active-template", () => HttpResponse.json(NO_CONSENT_TEMPLATE)),
  http.get("http://localhost/api/v1/intake-forms/active-template", () => HttpResponse.json(null)),
  http.post("http://localhost/api/v1/intake-forms", async ({ request }) => {
    capturedBody = (await request.json()) as Record<string, unknown>;
    return HttpResponse.json(SUBMITTED_FORM, { status: 201 });
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); capturedBody = undefined; });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                           authReducer,
      ui:                             uiReducer,
      [appointmentsApi.reducerPath]:  appointmentsApi.reducer,
      [consentFormsApi.reducerPath]:  consentFormsApi.reducer,
      [intakeFormsApi.reducerPath]:   intakeFormsApi.reducer,
    },
    middleware: (gd) => gd().concat(
      appointmentsApi.middleware, consentFormsApi.middleware, intakeFormsApi.middleware,
    ),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "client@test.com" }, token: "fake-token", tenantId: "s-001", role: "client", pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null, impersonationScopeError: null, impersonationSessionExpired: false },
    },
  });
}

function renderPage() {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter initialEntries={["/forms/intake/new"]}>
        <SubmitIntakeFormPage />
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("SubmitIntakeFormPage", () => {
  describe("no active template (fallback)", () => {
    it("renders the default single 'Medical history & notes' textarea", async () => {
      renderPage();
      expect(await screen.findByLabelText("Medical history & notes")).toBeInTheDocument();
    });

    it("does not render any dynamic field", async () => {
      renderPage();
      await screen.findByLabelText("Medical history & notes");
      expect(screen.queryByText("Allergies")).not.toBeInTheDocument();
    });

    it("shows the 10-character minimum error on too-short input", async () => {
      const user = userEvent.setup();
      renderPage();
      await user.type(await screen.findByLabelText("Medical history & notes"), "short");
      await user.click(screen.getByLabelText(/I consent/i));
      await user.click(screen.getByRole("button", { name: /submit intake form/i }));
      expect(await screen.findByText("Please provide at least 10 characters")).toBeInTheDocument();
    });

    it("submits formData as the raw textarea string", async () => {
      const user = userEvent.setup();
      renderPage();
      await user.type(await screen.findByLabelText("Medical history & notes"), "No known allergies at all");
      await user.click(screen.getByLabelText(/I consent/i));
      await user.click(screen.getByRole("button", { name: /submit intake form/i }));
      await screen.findByText("Intake form submitted!");
      expect(capturedBody?.formData).toBe("No known allergies at all");
    });
  });

  describe("with an active template", () => {
    beforeEach(() => {
      server.use(
        http.get("http://localhost/api/v1/intake-forms/active-template", () => HttpResponse.json(FULL_TEMPLATE)),
      );
    });

    it("renders all five field types instead of the fallback textarea", async () => {
      renderPage();
      expect(await screen.findByLabelText(/Allergies/)).toBeInTheDocument();
      expect(screen.queryByLabelText("Medical history & notes")).not.toBeInTheDocument();
      expect(screen.getByLabelText(/Notes/)).toBeInTheDocument();
      expect(screen.getByText("Skin type")).toBeInTheDocument();
      expect(screen.getByLabelText(/Confirm ok/)).toBeInTheDocument();
      expect(screen.getByLabelText(/DOB/)).toBeInTheDocument();
    });

    it("blocks submission when a required field is empty", async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByLabelText(/Allergies/);
      await user.click(screen.getByLabelText(/I consent/i));
      await user.click(screen.getByRole("button", { name: /submit intake form/i }));
      expect(await screen.findAllByText("This field is required.")).not.toHaveLength(0);
      expect(capturedBody).toBeUndefined();
    });

    it("submits formData as JSON keyed by field label", async () => {
      const user = userEvent.setup();
      renderPage();
      await user.type(await screen.findByLabelText(/Allergies/), "Peanuts");
      await user.click(screen.getByRole("combobox", { name: /skin type/i }));
      await user.click(await screen.findByRole("option", { name: "Oily" }));
      await user.click(screen.getByLabelText(/Confirm ok/));
      await user.click(screen.getByLabelText(/I consent/i));
      await user.click(screen.getByRole("button", { name: /submit intake form/i }));
      await screen.findByText("Intake form submitted!");

      const parsed = JSON.parse(capturedBody?.formData as string);
      expect(parsed.Allergies).toBe("Peanuts");
      expect(parsed["Confirm ok"]).toBe(true);
      expect(parsed["Skin type"]).toBe("Oily");
    });
  });
});
