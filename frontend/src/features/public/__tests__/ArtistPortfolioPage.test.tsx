import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";

import authReducer from "@/features/auth/authSlice";
import { ArtistPortfolioPage } from "@/features/public/components/ArtistPortfolioPage";
import type { PublicArtistResponse } from "@/features/public/publicApi";

// ── Mocks ──────────────────────────────────────────────────────────────────────

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useParams: () => ({ slug: "maria-silva" }) };
});

const mockUseGetPublicArtistQuery = vi.fn();

vi.mock("@/features/public/publicApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/public/publicApi")>();
  return {
    ...actual,
    useGetPublicArtistQuery: (...args: unknown[]) => mockUseGetPublicArtistQuery(...args),
  };
});

// ── Seed data ──────────────────────────────────────────────────────────────────

const ARTIST: PublicArtistResponse = {
  artistId:        "artist-001",
  name:            "Maria Silva",
  slug:            "maria-silva",
  bio:             "Specialises in neo-trad and blackwork.",
  portfolioImages: [
    "https://cdn.example.com/port1.jpg",
    "https://cdn.example.com/port2.jpg",
  ],
  studioName:     "Ink Soul",
  studioSlug:     "ink-soul",
  showBookingCta: true,
};

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: { auth: authReducer },
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    preloadedState: { auth: { user: null, token: null, tenantId: null, role: null } as any },
  });
}

function renderPage() {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter>
        <ArtistPortfolioPage />
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("ArtistPortfolioPage", () => {
  beforeEach(() => {
    mockUseGetPublicArtistQuery.mockReturnValue({ data: ARTIST, isLoading: false, isError: false });
  });

  it("renders artist name and bio when data loads", () => {
    renderPage();
    expect(screen.getByText("Maria Silva")).toBeInTheDocument();
    expect(screen.getByText("Specialises in neo-trad and blackwork.")).toBeInTheDocument();
  });

  it("renders portfolio images when present", () => {
    renderPage();
    const images = screen.getAllByRole("img");
    expect(images.length).toBeGreaterThanOrEqual(2);
  });

  it("shows 'Artist not found' when isError is true", () => {
    mockUseGetPublicArtistQuery.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    renderPage();
    expect(screen.getByText("Artist not found.")).toBeInTheDocument();
  });

  it("shows loading spinner while fetching", () => {
    mockUseGetPublicArtistQuery.mockReturnValue({ data: undefined, isLoading: true, isError: false });
    renderPage();
    expect(document.querySelector(".animate-spin")).toBeInTheDocument();
  });

  it("sets og:title meta tag with artist name", () => {
    renderPage();
    const ogTitle = document.head.querySelector('meta[property="og:title"]');
    expect(ogTitle?.getAttribute("content")).toContain("Maria Silva");
  });

  it("renders studio link back to /s/{studioSlug}", () => {
    renderPage();
    const studioLink = screen.getByRole("link", { name: /Ink Soul/i });
    expect(studioLink.getAttribute("href")).toContain("/s/ink-soul");
  });
});
