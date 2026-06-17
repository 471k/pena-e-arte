import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";

import authReducer from "@/features/auth/authSlice";
import { StudioPortfolioPage } from "@/features/public/components/StudioPortfolioPage";
import type { PublicStudioResponse } from "@/features/public/publicApi";

// ── Mocks ──────────────────────────────────────────────────────────────────────

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useParams: () => ({ slug: "test-studio" }) };
});

const mockUseGetPublicStudioQuery = vi.fn();

vi.mock("@/features/public/publicApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/public/publicApi")>();
  return {
    ...actual,
    useGetPublicStudioQuery: (...args: unknown[]) => mockUseGetPublicStudioQuery(...args),
  };
});

// ── Seed data ──────────────────────────────────────────────────────────────────

const STUDIO: PublicStudioResponse = {
  studioId:      "studio-001",
  name:          "Ink Soul",
  slug:          "test-studio",
  city:          "Porto",
  description:   "Premier tattoo studio in Porto.",
  coverImageUrl: "https://cdn.example.com/cover.jpg",
  artists:       [
    { artistId: "artist-001", name: "Maria Silva", slug: "maria-silva", bio: "Specialises in neo-trad." },
    { artistId: "artist-002", name: "João Costa",  slug: "joao-costa",  bio: null },
  ],
  showBookingCta: true,
};

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore(token: string | null = null) {
  return configureStore({
    reducer: { auth: authReducer },
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: null, token, tenantId: null, role: null } as any,
    },
  });
}

function renderPage(token: string | null = null) {
  render(
    <Provider store={makeStore(token)}>
      <MemoryRouter>
        <StudioPortfolioPage />
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("StudioPortfolioPage", () => {
  beforeEach(() => {
    mockUseGetPublicStudioQuery.mockReturnValue({ data: STUDIO, isLoading: false, isError: false });
  });

  it("renders studio name and city when data loads", () => {
    renderPage();
    expect(screen.getByText("Ink Soul")).toBeInTheDocument();
    expect(screen.getByText("Porto")).toBeInTheDocument();
  });

  it("renders cover image when coverImageUrl is present", () => {
    renderPage();
    const img = screen.getByRole("img", { name: "Ink Soul" });
    expect(img).toHaveAttribute("src", STUDIO.coverImageUrl);
  });

  it("renders artist cards for each artist in the list", () => {
    renderPage();
    expect(screen.getByText("Maria Silva")).toBeInTheDocument();
    expect(screen.getByText("João Costa")).toBeInTheDocument();
  });

  it("shows loading spinner while fetching", () => {
    mockUseGetPublicStudioQuery.mockReturnValue({ data: undefined, isLoading: true, isError: false });
    renderPage();
    expect(document.querySelector(".animate-spin")).toBeInTheDocument();
  });

  it("shows 'Studio not found' when isError is true", () => {
    mockUseGetPublicStudioQuery.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    renderPage();
    expect(screen.getByText("Studio not found.")).toBeInTheDocument();
  });

  it("renders 'Book here' CTA when showBookingCta is true", () => {
    renderPage();
    expect(screen.getByRole("link", { name: "Book here" })).toBeInTheDocument();
  });

  it("Book here links to /login redirect when unauthenticated", () => {
    renderPage(null);
    const link = screen.getByRole("link", { name: "Book here" });
    expect(link.getAttribute("href")).toMatch(/\/login/);
  });

  it("Book here links directly to /book when authenticated", () => {
    renderPage("fake-token");
    const link = screen.getByRole("link", { name: "Book here" });
    expect(link.getAttribute("href")).toMatch(/\/book/);
    expect(link.getAttribute("href")).not.toMatch(/\/login/);
  });

  it("sets og:title meta tag with studio name", () => {
    renderPage();
    const ogTitle = document.head.querySelector('meta[property="og:title"]');
    expect(ogTitle?.getAttribute("content")).toContain("Ink Soul");
  });

  it("sets canonical link tag with correct slug URL", () => {
    renderPage();
    const canonical = document.head.querySelector('link[rel="canonical"]');
    expect(canonical?.getAttribute("href")).toContain("test-studio");
  });
});
