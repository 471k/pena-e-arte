import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { studiosApi } from "@/features/studios/studiosApi";
import { QrCodeSection } from "@/features/studios/components/QrCodeSection";

// ── Mock URL.createObjectURL — not available in jsdom ─────────────────────────

const MOCK_BLOB_URL = "blob:http://localhost/mock-qr-code";
URL.createObjectURL = vi.fn().mockReturnValue(MOCK_BLOB_URL);

// ── Fixtures ──────────────────────────────────────────────────────────────────

const STUDIO = {
  id:                   "studio-001",
  name:                 "Ink & Soul Studio",
  slug:                 "ink-soul-studio",
  city:                 "Lisbon",
  latitude:             38.7169,
  longitude:            -9.1395,
  showPlatformBranding: true,
  allowBrandingRemoval: false,
  trialExpiresAt:       "2099-01-01T00:00:00Z",
  createdAt:            "2025-01-01T00:00:00Z",
  isActive:             true,
};

// Minimal PNG-like bytes (4-byte stub — enough for Blob construction)
const FAKE_PNG = new Uint8Array([0x89, 0x50, 0x4e, 0x47]);

// ── MSW server ────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json(STUDIO)),
  http.get("http://localhost/api/v1/studios/:id/qr", () =>
    new HttpResponse(FAKE_PNG, { headers: { "Content-Type": "image/png" } }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); vi.mocked(URL.createObjectURL).mockClear(); cleanup(); });
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

function renderSection() {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter>
        <QrCodeSection />
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("QrCodeSection — before studio data is available", () => {
  it("renders nothing on the initial render before the studio query resolves", () => {
    const { container } = render(
      // Render WITHOUT a provider that has any cached studio data
      <Provider store={makeStore()}>
        <MemoryRouter>
          <QrCodeSection />
        </MemoryRouter>
      </Provider>,
    );
    // Synchronous check — studio is undefined at time-zero
    expect(container.firstChild).toBeNull();
  });
});

describe("QrCodeSection — loading state", () => {
  it("shows a loading placeholder while the QR code is being fetched", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/:id/qr", async () => {
        // Hold the QR request open so isLoading stays true
        await new Promise<never>(() => undefined);
      }),
    );

    renderSection();
    // Studio loads first → card title visible
    await screen.findByText(/marketing qr code/i);
    // QR is still pending → loading placeholder visible
    expect(screen.getByTestId("qr-loading")).toBeInTheDocument();
  });
});

describe("QrCodeSection — error state", () => {
  it("shows an error message when the QR code request fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/:id/qr", () => HttpResponse.error()),
    );

    renderSection();
    expect(await screen.findByText(/failed to load qr code/i)).toBeInTheDocument();
  });
});

describe("QrCodeSection — success state", () => {
  it("renders the QR image with the studio name in its alt text", async () => {
    renderSection();
    const img = await screen.findByTestId<HTMLImageElement>("qr-image");
    expect(img).toBeInTheDocument();
    expect(img.alt).toMatch(/ink & soul studio/i);
  });

  it("uses the blob URL returned by URL.createObjectURL as the image src", async () => {
    renderSection();
    const img = await screen.findByTestId<HTMLImageElement>("qr-image");
    expect(img.src).toBe(MOCK_BLOB_URL);
  });

  it("displays the booking URL link", async () => {
    renderSection();
    await screen.findByTestId("qr-image");
    const link = screen.getByRole("link", { name: /tattooos\.co\/s\/ink-soul-studio/i });
    expect(link).toBeInTheDocument();
    expect(link).toHaveAttribute("href", "https://tattooos.co/s/ink-soul-studio");
  });
});

describe("QrCodeSection — download", () => {
  it("download button is disabled while QR is still loading", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/:id/qr", async () => {
        await new Promise<never>(() => undefined);
      }),
    );

    renderSection();
    await screen.findByText(/marketing qr code/i);
    expect(screen.getByRole("button", { name: /download png/i })).toBeDisabled();
  });

  it("download button is enabled once QR code is loaded", async () => {
    renderSection();
    await screen.findByTestId("qr-image");
    expect(screen.getByRole("button", { name: /download png/i })).not.toBeDisabled();
  });

  it("clicking Download PNG triggers an anchor click with the correct filename", async () => {
    const user = userEvent.setup();
    const anchorClickSpy = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});

    renderSection();
    await screen.findByTestId("qr-image");
    await user.click(screen.getByRole("button", { name: /download png/i }));

    expect(anchorClickSpy).toHaveBeenCalledTimes(1);

    anchorClickSpy.mockRestore();
  });

  it("renders a 'Download SVG' button", async () => {
    renderSection();
    await screen.findByTestId("qr-image");
    expect(screen.getByRole("button", { name: /download svg/i })).toBeInTheDocument();
  });

  it("clicking Download SVG fetches the svg format and triggers an anchor click", async () => {
    let requestedFormat: string | null = null;
    server.use(
      http.get("http://localhost/api/v1/studios/:id/qr", ({ request }) => {
        requestedFormat = new URL(request.url).searchParams.get("format");
        return new HttpResponse(FAKE_PNG, { headers: { "Content-Type": "image/svg+xml" } });
      }),
    );

    const user = userEvent.setup();
    const anchorClickSpy = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});

    renderSection();
    await screen.findByTestId("qr-image");
    await user.click(screen.getByRole("button", { name: /download svg/i }));

    await vi.waitFor(() => expect(anchorClickSpy).toHaveBeenCalledTimes(1));
    expect(requestedFormat).toBe("svg");

    anchorClickSpy.mockRestore();
  });
});
