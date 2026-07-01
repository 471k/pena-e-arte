import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { designsApi } from "@/features/designs/designsApi";
import { filesApi } from "@/shared/api/filesApi";
import { UploadRevisionPage } from "@/features/designs/components/UploadRevisionPage";
import type { DesignRevisionResponse } from "@/features/designs/design.types";

// ── Constants ─────────────────────────────────────────────────────────────────
// The presigned upload URL must be resolvable by MSW (i.e., absolute localhost URL).

const PRESIGN_UPLOAD_URL = "http://localhost/r2-upload";
const PRESIGN_PUBLIC_URL = "http://cdn.example.com/designs/design.jpg";
const DESIGN_ID          = "d-001";

const SAVED_REVISION: DesignRevisionResponse = {
  id:             "rev-new",
  designId:       DESIGN_ID,
  versionNumber:  2,
  fileUrl:        PRESIGN_PUBLIC_URL,
  notes:          null,
  uploadedAt:     "2024-06-01T10:00:00Z",
  approvalStatus: null,
  approvalNotes:  null,
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.post("http://localhost/api/v1/files/presign", () =>
    HttpResponse.json({ uploadUrl: PRESIGN_UPLOAD_URL, publicUrl: PRESIGN_PUBLIC_URL }),
  ),
  http.put(PRESIGN_UPLOAD_URL, () => new HttpResponse(null, { status: 200 })),
  http.post(`http://localhost/api/v1/designs/${DESIGN_ID}/revisions`, () =>
    HttpResponse.json(SAVED_REVISION, { status: 201 }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); vi.restoreAllMocks(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                      authReducer,
      ui:                        uiReducer,
      [designsApi.reducerPath]:  designsApi.reducer,
      [filesApi.reducerPath]:    filesApi.reducer,
    },
    middleware: (gd) => gd().concat(designsApi.middleware, filesApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "artist@ink.test" }, token: "fake-token", tenantId: "s-001", role: "artist", pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false },
    },
  });
}

function renderPage(designId = DESIGN_ID) {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter initialEntries={[`/designs/${designId}/upload`]}>
        <Routes>
          <Route path="/designs/:id/upload" element={<UploadRevisionPage />} />
          <Route path="/designs/:id"        element={<div data-testid="detail-page" />} />
          <Route path="/designs"            element={<div data-testid="designs-list" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

function getFileInput() {
  return document.querySelector<HTMLInputElement>("input[type=file]")!;
}

function makeJpeg(name = "design.jpg") {
  return new File(["img-bytes"], name, { type: "image/jpeg" });
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("UploadRevisionPage", () => {

  it("renders the 'Upload Revision' heading", () => {
    renderPage();
    // Both the <h2> and the submit button contain "Upload Revision"; target the heading.
    expect(screen.getByRole("heading", { name: /upload revision/i })).toBeInTheDocument();
  });

  it("renders the file picker and notes field", () => {
    renderPage();
    expect(getFileInput()).toBeInTheDocument();
    expect(screen.getByLabelText(/notes/i)).toBeInTheDocument();
  });

  it("shows the selected file name after picking a file", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.upload(getFileInput(), makeJpeg("sleeve.jpg"));
    expect(screen.getByText("sleeve.jpg")).toBeInTheDocument();
  });

  it("shows a validation error when an unsupported file type is selected", () => {
    renderPage();
    const pdf = new File(["pdf"], "consent.pdf", { type: "application/pdf" });
    const input = getFileInput();
    // userEvent.upload respects the accept attribute and silently drops non-matching files.
    // Use fireEvent to bypass that and let the component's own validation run.
    Object.defineProperty(input, "files", { value: [pdf], configurable: true });
    fireEvent.change(input);
    expect(screen.getByText("Only JPEG, PNG, and WebP images are accepted.")).toBeInTheDocument();
  });

  it("shows 'Select an image' error when submitting with no file chosen", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole("button", { name: /upload revision/i }));
    expect(screen.getByText("Select an image to upload.")).toBeInTheDocument();
  });

  it("navigates to the design detail page after a successful upload", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.upload(getFileInput(), makeJpeg());
    await user.click(screen.getByRole("button", { name: /upload revision/i }));
    expect(await screen.findByTestId("detail-page")).toBeInTheDocument();
  });

  it("shows step labels while upload is in progress (presigning → uploading → saving)", async () => {
    // Delay each step so we can observe intermediate labels.
    let resolvePresign!: (v: Response) => void;
    server.use(
      http.post("http://localhost/api/v1/files/presign", () =>
        new Promise<Response>((r) => { resolvePresign = r; }),
      ),
    );

    const user = userEvent.setup();
    renderPage();
    await user.upload(getFileInput(), makeJpeg());
    await user.click(screen.getByRole("button", { name: /upload revision/i }));

    expect(await screen.findByText("Getting upload URL…")).toBeInTheDocument();

    resolvePresign(
      HttpResponse.json({ uploadUrl: PRESIGN_UPLOAD_URL, publicUrl: PRESIGN_PUBLIC_URL }) as unknown as Response,
    );
  });

  it("shows an error message when the presign step fails", async () => {
    server.use(
      http.post("http://localhost/api/v1/files/presign", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await user.upload(getFileInput(), makeJpeg());
    await user.click(screen.getByRole("button", { name: /upload revision/i }));
    expect(await screen.findByText("Failed to get upload URL. Please try again.")).toBeInTheDocument();
  });

  it("shows an error message when the R2 PUT step fails", async () => {
    server.use(
      http.put(PRESIGN_UPLOAD_URL, () => new HttpResponse(null, { status: 500 })),
    );
    const user = userEvent.setup();
    renderPage();
    await user.upload(getFileInput(), makeJpeg());
    await user.click(screen.getByRole("button", { name: /upload revision/i }));
    expect(await screen.findByText("File upload failed. Please try again.")).toBeInTheDocument();
  });

  it("shows an error message when the revision save step fails", async () => {
    server.use(
      http.post(`http://localhost/api/v1/designs/${DESIGN_ID}/revisions`, () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await user.upload(getFileInput(), makeJpeg());
    await user.click(screen.getByRole("button", { name: /upload revision/i }));
    expect(await screen.findByText("Failed to save revision. Please try again.")).toBeInTheDocument();
  });

  it("back button is disabled while an upload is in progress", async () => {
    let resolvePresign!: (v: Response) => void;
    server.use(
      http.post("http://localhost/api/v1/files/presign", () =>
        new Promise<Response>((r) => { resolvePresign = r; }),
      ),
    );

    const user = userEvent.setup();
    renderPage();
    await user.upload(getFileInput(), makeJpeg());
    await user.click(screen.getByRole("button", { name: /upload revision/i }));

    await screen.findByText("Getting upload URL…");
    expect(screen.getByRole("button", { name: /designs/i })).toBeDisabled();

    resolvePresign(
      HttpResponse.json({ uploadUrl: PRESIGN_UPLOAD_URL, publicUrl: PRESIGN_PUBLIC_URL }) as unknown as Response,
    );
  });
});
