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
import { consentFormsApi } from "@/features/forms/consentFormsApi";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";

import { SignConsentFormPage } from "@/features/forms/components/SignConsentFormPage";
import { ConsentFormListPage } from "@/features/forms/components/ConsentFormListPage";
import { ConsentFormDetailPage } from "@/features/forms/components/ConsentFormDetailPage";

import type { ConsentFormResponse, ConsentFormDetailResponse } from "@/features/forms/form.types";
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

const SIGNED_FORM: ConsentFormResponse = {
  id: "cf-001", studioId: "s-001", clientId: "c-001",
  appointmentId: "appt-001",
  fileUrl: "https://r2.example.com/consent.pdf",
  signatureData: "M. Cliente",
  signedAt: "2024-02-01T10:00:00Z",
  createdAt: "2024-02-01T09:55:00Z",
  clientName: "Marco Cliente",
};

const PENDING_FORM: ConsentFormResponse = {
  id: "cf-002", studioId: "s-001", clientId: "c-002",
  appointmentId: "appt-002",
  fileUrl: null,
  signatureData: null,
  signedAt: null,
  createdAt: "2024-02-02T09:00:00Z",
  clientName: "Pending Client",
};

const SIGNED_FORM_DETAIL: ConsentFormDetailResponse = {
  ...SIGNED_FORM,
  appointmentDate: APPOINTMENT.date,
  artistName:      "Luca Artista",
  artistId:        "artist-001",
};

const PENDING_FORM_DETAIL: ConsentFormDetailResponse = {
  ...PENDING_FORM,
  appointmentDate: FUTURE,
  artistName:      null,
  artistId:        null,
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/appointments/mine", () => HttpResponse.json([APPOINTMENT])),
  http.post("http://localhost/api/v1/consent-forms", () => HttpResponse.json(SIGNED_FORM, { status: 201 })),
  http.get("http://localhost/api/v1/consent-forms", () => HttpResponse.json([SIGNED_FORM, PENDING_FORM])),
  http.get("http://localhost/api/v1/consent-forms/:id", ({ params }) =>
    params.id === SIGNED_FORM.id
      ? HttpResponse.json(SIGNED_FORM_DETAIL)
      : HttpResponse.json(PENDING_FORM_DETAIL),
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
      [consentFormsApi.reducerPath]: consentFormsApi.reducer,
      [appointmentsApi.reducerPath]: appointmentsApi.reducer,
    },
    middleware: (gd) =>
      gd().concat(consentFormsApi.middleware).concat(appointmentsApi.middleware),
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
      <MemoryRouter initialEntries={[`/forms/consent/${id}`]}>
        <Routes>
          <Route path="/forms/consent/:id" element={<ConsentFormDetailPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── SignConsentFormPage ────────────────────────────────────────────────────────

describe("SignConsentFormPage", () => {
  it("renders the page heading and form fields", async () => {
    renderWithRoute(<SignConsentFormPage />);
    expect(screen.getByText("Consent Form")).toBeInTheDocument();
    expect(screen.getByLabelText("Appointment")).toBeInTheDocument();
    expect(screen.getByLabelText(/digital signature/i)).toBeInTheDocument();
  });

  it("shows a validation error when submitted without an appointment or signature", async () => {
    const user = userEvent.setup();
    renderWithRoute(<SignConsentFormPage />);
    await user.click(screen.getByRole("button", { name: /sign consent form/i }));
    expect(await screen.findByText(/please select an appointment/i)).toBeInTheDocument();
    expect(screen.getByText(/please type your full name/i)).toBeInTheDocument();
  });

  it("lists appointments in the appointment selector", async () => {
    const user = userEvent.setup();
    renderWithRoute(<SignConsentFormPage />);
    await user.click(screen.getByLabelText("Appointment"));
    expect(await screen.findByRole("option", {
      name: new Date(FUTURE).toLocaleDateString("en-GB", {
        day: "numeric", month: "short", year: "numeric",
      }),
    })).toBeInTheDocument();
  });

  it("signs the consent form and shows a confirmation screen", async () => {
    const user = userEvent.setup();
    renderWithRoute(<SignConsentFormPage />);

    await user.click(screen.getByLabelText("Appointment"));
    await user.click(await screen.findByRole("option", {
      name: new Date(FUTURE).toLocaleDateString("en-GB", {
        day: "numeric", month: "short", year: "numeric",
      }),
    }));
    await user.type(screen.getByLabelText(/digital signature/i), "Marco Cliente");
    await user.click(screen.getByRole("button", { name: /sign consent form/i }));

    expect(await screen.findByText("Consent form signed!")).toBeInTheDocument();
  });

  it("shows an inline error message when signing fails", async () => {
    server.use(
      http.post("http://localhost/api/v1/consent-forms", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderWithRoute(<SignConsentFormPage />);

    await user.click(screen.getByLabelText("Appointment"));
    await user.click(await screen.findByRole("option", {
      name: new Date(FUTURE).toLocaleDateString("en-GB", {
        day: "numeric", month: "short", year: "numeric",
      }),
    }));
    await user.type(screen.getByLabelText(/digital signature/i), "Marco Cliente");
    await user.click(screen.getByRole("button", { name: /sign consent form/i }));

    expect(await screen.findByText(/failed to sign\. please try again\./i)).toBeInTheDocument();
  });
});

// ── ConsentFormListPage ────────────────────────────────────────────────────────

describe("ConsentFormListPage", () => {
  it("shows loading skeletons while fetching", () => {
    renderWithRoute(<ConsentFormListPage />, "/forms/consent");
    expect(document.querySelectorAll(".animate-pulse").length).toBe(5);
    expect(screen.getByText("Consent Forms")).toBeInTheDocument();
  });

  it("shows an error message when the fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/consent-forms", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    renderWithRoute(<ConsentFormListPage />, "/forms/consent");
    expect(await screen.findByText(/failed to load consent forms/i)).toBeInTheDocument();
  });

  it("shows rich empty state when there are no forms and no filters", async () => {
    server.use(
      http.get("http://localhost/api/v1/consent-forms", () => HttpResponse.json([])),
    );
    renderWithRoute(<ConsentFormListPage />, "/forms/consent");
    expect(await screen.findByText(/no signed consent forms yet/i)).toBeInTheDocument();
  });

  it("shows the 'Signed' badge for a signed form and 'Pending' otherwise", async () => {
    renderWithRoute(<ConsentFormListPage />, "/forms/consent");
    await screen.findByText("Signed");
    expect(screen.getByText("Pending")).toBeInTheDocument();
  });

  it("shows the form count in the header", async () => {
    renderWithRoute(<ConsentFormListPage />, "/forms/consent");
    expect(await screen.findByText("2 forms")).toBeInTheDocument();
  });

  it("shows a filter banner when appointmentId is present in the query string", async () => {
    renderWithRoute(<ConsentFormListPage />, "/forms/consent?appointmentId=appt-001");
    expect(await screen.findByText("appt-001")).toBeInTheDocument();
  });
});

// ── ConsentFormDetailPage ──────────────────────────────────────────────────────

describe("ConsentFormDetailPage", () => {
  it("shows a loading skeleton then renders the signed form", async () => {
    renderDetailPage(SIGNED_FORM.id);
    expect(screen.getByLabelText(/loading consent form/i)).toBeInTheDocument();
    // Now shows client name, not raw UUID
    expect(await screen.findByText("Marco Cliente")).toBeInTheDocument();
    expect(screen.getAllByText("Signed").length).toBeGreaterThanOrEqual(1);
  });

  it("renders a document link when fileUrl is present", async () => {
    renderDetailPage(SIGNED_FORM.id);
    // fileUrl ends in .pdf, so the label is the PDF-specific variant
    const link = await screen.findByRole("link", { name: /view signed consent \(pdf\)/i });
    expect(link).toHaveAttribute("href", SIGNED_FORM.fileUrl);
  });

  it("shows the 'Pending' badge and no signature for an unsigned form", async () => {
    renderDetailPage(PENDING_FORM.id);
    expect(await screen.findByText("Pending")).toBeInTheDocument();
    expect(screen.queryByText("Digital signature")).not.toBeInTheDocument();
  });

  it("shows an error message when the fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/consent-forms/:id", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    renderDetailPage(SIGNED_FORM.id);
    expect(await screen.findByText(/failed to load consent form/i)).toBeInTheDocument();
  });

  it("renders base64 signatureData as an <img> not as text", async () => {
    const base64Sig = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
    server.use(
      http.get("http://localhost/api/v1/consent-forms/:id", () =>
        HttpResponse.json({ ...SIGNED_FORM_DETAIL, signatureData: base64Sig }),
      ),
    );
    renderDetailPage(SIGNED_FORM.id);
    const img = await screen.findByRole("img", { name: /digital signature/i });
    expect(img).toHaveAttribute("src", base64Sig);
    // The raw base64 string must NOT appear as visible text
    expect(screen.queryByText(base64Sig)).not.toBeInTheDocument();
  });

  it("renders typed-name signatureData as italic text not as an image", async () => {
    renderDetailPage(SIGNED_FORM.id);
    // The typed signature should appear as text (not as an img src)
    expect(await screen.findByText("M. Cliente")).toBeInTheDocument();
    expect(screen.queryByRole("img", { name: /digital signature/i })).not.toBeInTheDocument();
  });

  it("shows a 'not found' message on 404", async () => {
    server.use(
      http.get("http://localhost/api/v1/consent-forms/:id", () =>
        HttpResponse.json({ message: "Not found" }, { status: 404 }),
      ),
    );
    renderDetailPage("nonexistent-id");
    expect(await screen.findByText(/consent form not found/i)).toBeInTheDocument();
  });

  it("shows a download link when fileUrl ends in .pdf", async () => {
    renderDetailPage(SIGNED_FORM.id);
    expect(await screen.findByRole("link", { name: /download/i })).toBeInTheDocument();
  });

  it("shows a relative timestamp alongside the absolute date", async () => {
    renderDetailPage(SIGNED_FORM.id);
    // formatRelative returns strings like "Xd ago", "Xmo ago" — check for "ago"
    const agos = await screen.findAllByText(/ago/);
    expect(agos.length).toBeGreaterThanOrEqual(1);
  });

  it("shows client name as a link to the client profile", async () => {
    renderDetailPage(SIGNED_FORM.id);
    await screen.findByText("Marco Cliente");
    const link = screen.getByRole("link", { name: "Marco Cliente" });
    expect(link).toHaveAttribute("href", expect.stringContaining(SIGNED_FORM.clientId));
  });
});
