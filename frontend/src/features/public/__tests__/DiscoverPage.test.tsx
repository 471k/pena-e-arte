import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { Provider }     from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { publicApi }  from "@/features/public/publicApi";
import { DiscoverPage } from "@/features/public/components/DiscoverPage";

// ── Helpers ────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: { [publicApi.reducerPath]: publicApi.reducer },
    middleware: (gd) => gd().concat(publicApi.middleware),
  });
}

function renderPage() {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter>
        <DiscoverPage />
      </MemoryRouter>
    </Provider>,
  );
}

// Stub navigator.geolocation — simulate denied permission by default
Object.defineProperty(navigator, "geolocation", {
  value: {
    getCurrentPosition: vi.fn().mockImplementation((_success: unknown, error: ((e: unknown) => void) | undefined) => {
      if (error) error({ code: 1, message: "User denied geolocation" });
    }),
  },
  configurable: true,
});

// ── MSW ───────────────────────────────────────────────────────────────────

const STUDIOS = [
  {
    studioId:      "studio-1",
    name:          "Ink & Soul",
    slug:          "ink-soul",
    city:          "Lisbon",
    coverImageUrl: null,
    distanceKm:    2.4,
    artistCount:   3,
    averageRating: 4.5,
    reviewCount:   12,
  },
];

const server = setupServer(
  http.get("http://localhost/api/v1/public/studios/nearby", () =>
    HttpResponse.json(STUDIOS),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "warn" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Tests ──────────────────────────────────────────────────────────────────

describe("DiscoverPage", () => {
  it("renders the heading", () => {
    renderPage();
    expect(screen.getByRole("heading", { name: /find tattoo studios near you/i })).toBeInTheDocument();
  });

  it("renders 'View on map' and 'Sign in' nav links", () => {
    renderPage();
    expect(screen.getByRole("link", { name: /view on map/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /sign in/i })).toBeInTheDocument();
  });

  it("'Register your studio' nav link does NOT use a filled white style", () => {
    renderPage();
    const link = screen.getByRole("link", { name: /register your studio/i });
    expect(link.className).not.toMatch(/bg-foreground/);
    expect(link.className).not.toMatch(/bg-white/);
    expect(link.className).toMatch(/violet/);
  });

  it("does NOT render a PenLine icon inside the image placeholder area", () => {
    renderPage();
    const penLineElements = document.querySelectorAll("[data-lucide='pen-line']");
    expect(penLineElements).toHaveLength(0);
  });

  it("renders the studio name in the card", async () => {
    renderPage();
    expect(await screen.findByText("Ink & Soul")).toBeInTheDocument();
  });

  it("renders star rating when reviewCount > 0", async () => {
    renderPage();
    await screen.findByText("Ink & Soul");
    expect(screen.getByRole("img", { name: /rating/i })).toBeInTheDocument();
    expect(screen.getByText("(12)")).toBeInTheDocument();
  });

  it("renders 'No reviews yet' when reviewCount is 0", async () => {
    server.use(
      http.get("http://localhost/api/v1/public/studios/nearby", () =>
        HttpResponse.json([{ ...STUDIOS[0], reviewCount: 0, averageRating: null }]),
      ),
    );
    renderPage();
    await screen.findByText("Ink & Soul");
    expect(screen.getByText(/no reviews yet/i)).toBeInTheDocument();
  });

  it("result count has aria-live=polite", async () => {
    renderPage();
    await screen.findByText("Ink & Soul");
    const count = screen.getByText(/\d+ studio.*near/i);
    expect(count).toHaveAttribute("aria-live", "polite");
  });

  it("result count includes the location reference", async () => {
    renderPage();
    await screen.findByText("Ink & Soul");
    expect(screen.getByText(/1 studio near/i)).toBeInTheDocument();
  });

  it("empty state renders when API returns no studios", async () => {
    server.use(
      http.get("http://localhost/api/v1/public/studios/nearby", () =>
        HttpResponse.json([]),
      ),
    );
    renderPage();
    expect(await screen.findByText(/no studios found nearby/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Register your studio →" })).toBeInTheDocument();
  });

  it("skeleton renders while loading", () => {
    renderPage();
    expect(screen.getByLabelText("Loading studios")).toBeInTheDocument();
  });

  it("search button is accessible", () => {
    renderPage();
    expect(screen.getByRole("button", { name: /search location/i })).toBeInTheDocument();
  });

  it("search error shows when Nominatim returns no results", async () => {
    server.use(
      http.get("https://nominatim.openstreetmap.org/search", () =>
        HttpResponse.json([]),
      ),
    );

    const user = userEvent.setup();
    renderPage();

    // Wait for studios to load so the RTK query is settled before searching
    await screen.findByText("Ink & Soul");

    const input = screen.getByLabelText(/search for a city/i);
    await user.type(input, "Nowhere Land");
    await user.click(screen.getByRole("button", { name: /search location/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/location not found/i);
  });
});
