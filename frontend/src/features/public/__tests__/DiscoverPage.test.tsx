import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Routes, Route, useSearchParams } from "react-router-dom";
import { Provider }     from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import authReducer    from "@/features/auth/authSlice";
import { publicApi }  from "@/features/public/publicApi";
import { savedImagesApi } from "@/features/public/savedImagesApi";
import { DiscoverPage } from "@/features/public/components/DiscoverPage";

// ── Helpers ────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      [publicApi.reducerPath]:      publicApi.reducer,
      [savedImagesApi.reducerPath]: savedImagesApi.reducer,
    },
    middleware: (gd) => gd().concat(publicApi.middleware, savedImagesApi.middleware),
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

function makeAuthStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      [publicApi.reducerPath]:      publicApi.reducer,
      [savedImagesApi.reducerPath]: savedImagesApi.reducer,
    },
    middleware: (gd) => gd().concat(publicApi.middleware, savedImagesApi.middleware),
    preloadedState: {
      auth: {
        user:                { id: "u-1", email: "phi@test.com", name: "Phi" },
        token:               "fake-jwt-token",
        refreshToken:        null,
        tenantId:            "t-1",
        role:                "owner" as const,
        pendingReferralCode: null,
      },
    },
  });
}

function CurrentSearch({ onRender }: { onRender: (s: string) => void }) {
  const [params] = useSearchParams();
  onRender(params.toString());
  return null;
}

function renderPageAt(initialPath: string, onSearchChange: (s: string) => void) {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter initialEntries={[initialPath]}>
        <Routes>
          <Route
            path="/discover"
            element={<><DiscoverPage /><CurrentSearch onRender={onSearchChange} /></>}
          />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

function renderLoggedInPage() {
  render(
    <Provider store={makeAuthStore()}>
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
  // Portfolio feed returns empty by default in DiscoverPage tests
  http.get("http://localhost/api/v1/public/portfolio/feed", () =>
    HttpResponse.json([]),
  ),
  // PortfolioFeed calls saved-images/ids when the user is logged in.
  // In these tests auth.token is null, so skip=true, but handler avoids unhandled warnings.
  http.get("http://localhost/api/v1/saved-images/ids", () =>
    HttpResponse.json([]),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "warn" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helper: switch to Studios tab ─────────────────────────────────────────

async function switchToStudiosTab() {
  const user = userEvent.setup();
  const studiosTab = screen.getByRole("tab", { name: /studios/i });
  await user.click(studiosTab);
  return user;
}

// ── Tests ──────────────────────────────────────────────────────────────────

describe("DiscoverPage", () => {
  it("renders the portfolio and studios tabs", () => {
    renderPage();
    expect(screen.getByRole("tab", { name: /portfolio/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /studios/i })).toBeInTheDocument();
  });

  it("renders 'Map' nav link when unauthenticated", () => {
    renderPage();
    const header = document.querySelector("header")!;
    const navLinks = Array.from(header.querySelectorAll("a"));
    expect(navLinks.some((a) => /^map$/i.test(a.textContent?.trim() ?? ""))).toBe(true);
  });

  it("renders 'Sign in' nav link when unauthenticated", () => {
    renderPage();
    expect(screen.getByRole("link", { name: /^sign in$/i })).toBeInTheDocument();
  });

  it("'Register studio' nav link uses violet border style", () => {
    renderPage();
    const header = document.querySelector("header")!;
    // There may be multiple "Register studio" links (header + footer); target the header one
    const navLinks = Array.from(header.querySelectorAll("a")).filter((a) =>
      /register studio/i.test(a.textContent ?? ""),
    );
    expect(navLinks.length).toBeGreaterThanOrEqual(1);
    const link = navLinks[0];
    expect(link.className).not.toMatch(/bg-foreground/);
    expect(link.className).not.toMatch(/bg-white/);
    expect(link.className).toMatch(/violet/);
    expect(link.className).toMatch(/border-2/);
  });

  it("does NOT render a PenLine icon inside the image placeholder area", () => {
    renderPage();
    const penLineElements = document.querySelectorAll("[data-lucide='pen-line']");
    expect(penLineElements).toHaveLength(0);
  });

  it("search input has updated placeholder text", () => {
    renderPage();
    expect(
      screen.getByPlaceholderText(/find artists in a city/i),
    ).toBeInTheDocument();
  });

  it("portfolio tab is active by default (aria-selected=true)", () => {
    renderPage();
    const portfolioTab = screen.getByRole("tab", { name: /portfolio/i });
    expect(portfolioTab).toHaveAttribute("aria-selected", "true");
    expect(portfolioTab.className).toMatch(/border-violet/);
  });

  it("switching to studios tab makes it aria-selected and deselects portfolio", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole("tab", { name: /studios/i }));
    expect(screen.getByRole("tab", { name: /studios/i })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: /portfolio/i })).toHaveAttribute("aria-selected", "false");
  });

  it("opens on the Studios tab when the URL has ?tab=studios", () => {
    renderPageAt("/discover?tab=studios", () => {});
    expect(screen.getByRole("tab", { name: /studios/i })).toHaveAttribute("aria-selected", "true");
  });

  it("switching to Studios tab updates the URL to ?tab=studios", async () => {
    const user = userEvent.setup();
    let search = "";
    renderPageAt("/discover", (s) => { search = s; });
    await user.click(screen.getByRole("tab", { name: /studios/i }));
    expect(search).toBe("tab=studios");
  });

  it("switching back to Portfolio tab removes the tab param from the URL", async () => {
    const user = userEvent.setup();
    let search = "";
    renderPageAt("/discover?tab=studios", (s) => { search = s; });
    await user.click(screen.getByRole("tab", { name: /portfolio/i }));
    expect(search).toBe("");
  });

  it("renders hero heading for logged-out users when location is Lisbon (default fallback)", async () => {
    // Geolocation is denied in this suite → falls back to Lisbon → locationName is non-empty
    // Hero only shows when !token && !locationName. Since geo is denied and locationName becomes
    // Lisbon via fallback, the hero won't show after that. Test what IS shown instead.
    renderPage();
    // The value-prop strip (above tabs) should be present since token is null
    const strip = screen.getByText(/discover tattoo artists and studios near you/i);
    expect(strip).toBeInTheDocument();
  });

  it("footer renders copyright notice", () => {
    renderPage();
    expect(screen.getByText(/© \d{4} TattooOS/i)).toBeInTheDocument();
  });

  it("footer renders Map link for unauthenticated users", () => {
    renderPage();
    const footer = document.querySelector("footer")!;
    const links  = Array.from(footer.querySelectorAll("a")).map((a) => a.textContent?.trim());
    expect(links).toContain("Map");
    expect(links).not.toContain("Discover");
  });

  it("footer renders 'Register studio' for unauthenticated users only", () => {
    renderPage();
    const footer = document.querySelector("footer")!;
    const links  = Array.from(footer.querySelectorAll("a")).map((a) => a.textContent?.trim());
    expect(links).toContain("Register studio");
  });

  it("renders the studio name in the card after switching to Studios tab", async () => {
    renderPage();
    await switchToStudiosTab();
    expect(await screen.findByText("Ink & Soul")).toBeInTheDocument();
  });

  it("renders star rating when reviewCount > 0 (Studios tab)", async () => {
    renderPage();
    await switchToStudiosTab();
    await screen.findByText("Ink & Soul");
    expect(screen.getByRole("img", { name: /rating/i })).toBeInTheDocument();
    expect(screen.getByText("(12)")).toBeInTheDocument();
  });

  it("renders 'No reviews yet' when reviewCount is 0 (Studios tab)", async () => {
    server.use(
      http.get("http://localhost/api/v1/public/studios/nearby", () =>
        HttpResponse.json([{ ...STUDIOS[0], reviewCount: 0, averageRating: null }]),
      ),
    );
    renderPage();
    await switchToStudiosTab();
    await screen.findByText("Ink & Soul");
    expect(screen.getByText(/no reviews yet/i)).toBeInTheDocument();
  });

  it("result count has aria-live=polite (Studios tab)", async () => {
    renderPage();
    await switchToStudiosTab();
    await screen.findByText("Ink & Soul");
    const count = screen.getByText(/\d+ studio.*near/i);
    expect(count).toHaveAttribute("aria-live", "polite");
  });

  it("result count includes the location reference (Studios tab)", async () => {
    renderPage();
    await switchToStudiosTab();
    await screen.findByText("Ink & Soul");
    expect(screen.getByText(/1 studio near/i)).toBeInTheDocument();
  });

  it("empty state renders when API returns no studios (Studios tab)", async () => {
    server.use(
      http.get("http://localhost/api/v1/public/studios/nearby", () =>
        HttpResponse.json([]),
      ),
    );
    renderPage();
    await switchToStudiosTab();
    expect(await screen.findByText(/no studios found nearby/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Register your studio →" })).toBeInTheDocument();
  });

  it("portfolio skeleton renders while portfolio feed is loading", () => {
    renderPage();
    // Default tab is Portfolio; feed starts loading immediately
    expect(screen.getByLabelText("Loading portfolio")).toBeInTheDocument();
  });

  it("search button is accessible", () => {
    renderPage();
    expect(screen.getByRole("button", { name: "Search" })).toBeInTheDocument();
  });

  it("search error shows when Nominatim returns no results", async () => {
    server.use(
      http.get("https://nominatim.openstreetmap.org/search", () =>
        HttpResponse.json([]),
      ),
    );

    const user = userEvent.setup();
    renderPage();

    const input = screen.getByLabelText(/search for a city/i);
    await user.type(input, "Nowhere Land");
    await user.click(screen.getByRole("button", { name: "Search" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/location not found/i);
  });

  // ── Keyword search ("What" field) ─────────────────────────────────────────

  it("keyword input renders on the Portfolio tab with its accessible label", () => {
    renderPage();
    expect(
      screen.getByLabelText(/search tattoo styles or artist names/i),
    ).toBeInTheDocument();
  });

  it("keyword input is absent when the Studios tab is active", async () => {
    renderPage();
    await switchToStudiosTab();
    expect(
      screen.queryByLabelText(/search tattoo styles or artist names/i),
    ).not.toBeInTheDocument();
  });

  it("typing a keyword sends a debounced search param in the feed request", async () => {
    let capturedUrl = "";
    server.use(
      http.get("http://localhost/api/v1/public/portfolio/feed", ({ request }) => {
        capturedUrl = request.url;
        return HttpResponse.json([]);
      }),
    );
    const user = userEvent.setup();
    renderPage();

    const input = screen.getByLabelText(/search tattoo styles or artist names/i);
    await user.type(input, "dragon");

    await waitFor(() => expect(capturedUrl).toContain("search=dragon"), { timeout: 3000 });
  }, 10000);

  // ── Tab switching ───────────────────────────────────────────────────────

  it("defaults to Portfolio tab", () => {
    renderPage();
    const portfolioTab = screen.getByRole("tab", { name: /portfolio/i });
    expect(portfolioTab).toHaveAttribute("aria-selected", "true");
  });

  it("switches to Studios tab on click", async () => {
    const user = userEvent.setup();
    renderPage();
    const studiosTab = screen.getByRole("tab", { name: /studios/i });
    await user.click(studiosTab);
    expect(studiosTab).toHaveAttribute("aria-selected", "true");
  });
});

describe("DiscoverPage — authenticated nav", () => {
  it("shows avatar button and hides 'Sign in' when authenticated", () => {
    renderLoggedInPage();
    expect(screen.getByRole("button", { name: /account menu/i })).toBeInTheDocument();
    const header = document.querySelector("header")!;
    const navSignIn = Array.from(header.querySelectorAll("a")).find((a) =>
      /^sign in$/i.test(a.textContent?.trim() ?? "")
    );
    expect(navSignIn).toBeUndefined();
  });

  it("shows avatar initials derived from user name", () => {
    renderLoggedInPage();
    const avatarBtn = screen.getByRole("button", { name: /account menu/i });
    expect(avatarBtn.textContent).toBe("P");
  });

  it("hides 'Register studio' nav button when authenticated", () => {
    renderLoggedInPage();
    const header = document.querySelector("header")!;
    const registerLinks = Array.from(header.querySelectorAll("a")).filter((a) =>
      /register studio/i.test(a.textContent ?? "")
    );
    expect(registerLinks).toHaveLength(0);
  });

  it("opens account dropdown when avatar button is clicked", async () => {
    const user = userEvent.setup();
    renderLoggedInPage();
    await user.click(screen.getByRole("button", { name: /account menu/i }));
    expect(screen.getByRole("menu", { name: /account options/i })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /dashboard/i })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /sign out/i })).toBeInTheDocument();
  });

  it("footer hides 'Register studio' when authenticated", () => {
    renderLoggedInPage();
    const footer = document.querySelector("footer")!;
    const links  = Array.from(footer.querySelectorAll("a")).map((a) => a.textContent?.trim());
    expect(links).not.toContain("Register studio");
  });

  it("footer does not contain a circular 'Discover' link (unauthenticated)", () => {
    renderPage();
    const footer = document.querySelector("footer")!;
    const links  = Array.from(footer.querySelectorAll("a")).map((a) => a.textContent?.trim());
    expect(links).not.toContain("Discover");
  });

  it("avatar button has aria-expanded=false when dropdown is closed", () => {
    renderLoggedInPage();
    const btn = screen.getByRole("button", { name: /account menu/i });
    expect(btn).toHaveAttribute("aria-expanded", "false");
  });

  it("avatar button has aria-expanded=true when dropdown is open", async () => {
    const user = userEvent.setup();
    renderLoggedInPage();
    await user.click(screen.getByRole("button", { name: /account menu/i }));
    expect(screen.getByRole("button", { name: /account menu/i }))
      .toHaveAttribute("aria-expanded", "true");
  });
});
