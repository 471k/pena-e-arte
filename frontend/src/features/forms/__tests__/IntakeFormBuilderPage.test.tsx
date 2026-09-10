import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { Toaster } from "sonner";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { intakeFormsApi } from "@/features/forms/intakeFormsApi";
import { IntakeFormBuilderPage } from "@/features/forms/components/IntakeFormBuilderPage";

const EXISTING_TEMPLATE = {
  id: "tpl-1",
  studioId: "s-001",
  isActive: true,
  createdAt: "2024-01-01T00:00:00Z",
  updatedAt: "2024-01-01T00:00:00Z",
  fieldSchemaJson: JSON.stringify([
    { label: "Allergies", type: "Text", required: true },
  ]),
};

const server = setupServer(
  http.get("http://localhost/api/v1/intake-forms/template/mine", () => HttpResponse.json(null)),
  http.put("http://localhost/api/v1/intake-forms/template", () =>
    HttpResponse.json(EXISTING_TEMPLATE)),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      ui:   uiReducer,
      [intakeFormsApi.reducerPath]: intakeFormsApi.reducer,
    },
    middleware: (gd) => gd().concat(intakeFormsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "owner@test.com" }, token: "fake-token", tenantId: "s-001", role: "owner", pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null },
    },
  });
}

function renderPage() {
  render(
    <Provider store={makeStore()}>
      <Toaster />
      <MemoryRouter initialEntries={["/intake-form-builder"]}>
        <IntakeFormBuilderPage />
      </MemoryRouter>
    </Provider>,
  );
}

describe("IntakeFormBuilderPage", () => {
  it("renders one empty field row by default when no template exists", async () => {
    renderPage();
    expect(await screen.findByLabelText("Label")).toBeInTheDocument();
  });

  it("loads an existing template's fields for editing", async () => {
    server.use(
      http.get("http://localhost/api/v1/intake-forms/template/mine", () => HttpResponse.json(EXISTING_TEMPLATE)),
    );
    renderPage();
    expect(await screen.findByDisplayValue("Allergies")).toBeInTheDocument();
  });

  it("Add field adds another row", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByLabelText("Label");
    await user.click(screen.getByRole("button", { name: /add field/i }));
    expect(screen.getAllByLabelText("Label")).toHaveLength(2);
  });

  it("blocks save when a field label is empty", async () => {
    renderPage();
    await screen.findByLabelText("Label");
    expect(screen.getByRole("button", { name: /save intake form/i })).toBeDisabled();
  });

  it("saves successfully and shows a success toast", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(await screen.findByLabelText("Label"), "Allergies");
    await user.click(screen.getByRole("button", { name: /save intake form/i }));
    expect(await screen.findByText("Intake form saved.")).toBeInTheDocument();
  });
});
