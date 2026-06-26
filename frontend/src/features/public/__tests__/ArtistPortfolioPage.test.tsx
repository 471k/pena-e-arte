import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
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

const mockUseGetPublicArtistQuery  = vi.fn();
const mockUseGetArtistReviewsQuery = vi.fn();

vi.mock("@/features/public/publicApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/public/publicApi")>();
  return {
    ...actual,
    useGetPublicArtistQuery:       (...args: unknown[]) => mockUseGetPublicArtistQuery(...args),
    useGetArtistReviewsQuery:      (...args: unknown[]) => mockUseGetArtistReviewsQuery(...args),
    useCreateStudioReviewMutation: () => [vi.fn(), { isLoading: false }],
    useCreateArtistReviewMutation: () => [vi.fn(), { isLoading: false }],
    useRecordArtistViewMutation:   () => [vi.fn(), { isLoading: false }],
  };
});

// ── Seed data ──────────────────────────────────────────────────────────────────

const ARTIST: PublicArtistResponse = {
  artistId:        "artist-001",
  name:            "Maria Silva",
  slug:            "maria-silva",
  bio:             "Specialises in neo-trad and blackwork.",
  profileImageUrl: null,
  portfolioImages: [
    "https://cdn.example.com/port1.jpg",
    "https://cdn.example.com/port2.jpg",
  ],
  specializations: "Blackwork, Neo-Trad",
  hourlyRate:      120,
  averageRating:   4.5,
  reviewCount:     8,
  studioName:      "Ink Soul",
  studioSlug:      "ink-soul",
  showBookingCta:  true,
  isOwnProfile:    false,
};

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore(token: string | null = null) {
  return configureStore({
    reducer: { auth: authReducer },
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    preloadedState: { auth: { user: null, token, tenantId: null, role: null } as any },
  });
}

function renderPage(token: string | null = null) {
  render(
    <Provider store={makeStore(token)}>
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
    mockUseGetArtistReviewsQuery.mockReturnValue({ data: [], isLoading: false });
  });

  it("renders artist name", () => {
    renderPage();
    expect(screen.getByRole("heading", { name: "Maria Silva" })).toBeInTheDocument();
  });

  it("sets og:title meta tag with artist name", () => {
    renderPage();
    const ogTitle = document.head.querySelector('meta[property="og:title"]');
    expect(ogTitle?.getAttribute("content")).toContain("Maria Silva");
  });

  it("renders monogram avatar when profileImageUrl is null", () => {
    renderPage();
    // No <img> with profile photo alt text — only portfolio images
    const imgs = screen.queryAllByRole("img");
    const profileImg = imgs.find((img) =>
      img.getAttribute("alt")?.includes("Profile photo"),
    );
    expect(profileImg).toBeUndefined();
  });

  it("renders profile photo when profileImageUrl is set", () => {
    const artistWithAvatar = { ...ARTIST, profileImageUrl: "https://cdn.example.com/avatar.jpg" };
    mockUseGetPublicArtistQuery.mockReturnValue({ data: artistWithAvatar, isLoading: false, isError: false });
    renderPage();
    const profileImg = screen.getByAltText("Profile photo of Maria Silva");
    expect(profileImg).toBeInTheDocument();
    expect(profileImg.getAttribute("src")).toBe("https://cdn.example.com/avatar.jpg");
  });

  it("renders rating badge when reviewCount > 0", () => {
    renderPage();
    expect(screen.getByText(/8 review/i)).toBeInTheDocument();
  });

  it("renders 'Be the first to review' when reviewCount === 0", () => {
    mockUseGetPublicArtistQuery.mockReturnValue({
      data: { ...ARTIST, reviewCount: 0, averageRating: null },
      isLoading: false,
      isError: false,
    });
    renderPage();
    expect(screen.getByText(/be the first to review/i)).toBeInTheDocument();
  });

  it("renders specialization chips when specializations is set", () => {
    renderPage();
    expect(screen.getByText("Blackwork")).toBeInTheDocument();
    expect(screen.getByText("Neo-Trad")).toBeInTheDocument();
  });

  it("does not render specialization chips when specializations is null", () => {
    mockUseGetPublicArtistQuery.mockReturnValue({
      data: { ...ARTIST, specializations: null },
      isLoading: false,
      isError: false,
    });
    renderPage();
    expect(screen.queryByText("Blackwork")).not.toBeInTheDocument();
  });

  it("renders hourly rate when hourlyRate is not null", () => {
    renderPage();
    expect(screen.getByText(/€120\/hr/i)).toBeInTheDocument();
  });

  it("does not render hourly rate when hourlyRate is null", () => {
    mockUseGetPublicArtistQuery.mockReturnValue({
      data: { ...ARTIST, hourlyRate: null },
      isLoading: false,
      isError: false,
    });
    renderPage();
    expect(screen.queryByText(/\/hr/i)).not.toBeInTheDocument();
  });

  it("renders 'Book an Appointment' as CTA text", () => {
    renderPage();
    expect(screen.getByRole("link", { name: /book an appointment/i })).toBeInTheDocument();
  });

  it("book button redirects to login when not authenticated", () => {
    renderPage(null);
    const bookLink = screen.getByRole("link", { name: /book an appointment/i });
    const href = bookLink.getAttribute("href") ?? "";
    expect(href).toContain("/login");
    // /book is URL-encoded in the redirect param
    expect(decodeURIComponent(href)).toContain("/book");
  });

  it("book button links directly to booking when authenticated", () => {
    renderPage("test-token");
    const bookLink = screen.getByRole("link", { name: /book an appointment/i });
    expect(bookLink.getAttribute("href")).toContain("/book");
    expect(bookLink.getAttribute("href")).not.toContain("/login");
  });

  it("renders portfolio images as buttons with aria-labels", () => {
    renderPage();
    const imageButtons = screen.getAllByRole("button", { name: /view portfolio image/i });
    expect(imageButtons).toHaveLength(2);
  });

  it("clicking a portfolio image opens the lightbox", () => {
    renderPage();
    const imageButtons = screen.getAllByRole("button", { name: /view portfolio image/i });
    fireEvent.click(imageButtons[0]);
    const lightboxImg = screen.getByAltText(/tattoo portfolio by maria silva/i);
    expect(lightboxImg).toBeInTheDocument();
  });

  it("renders profile strength nudge only when isOwnProfile is true", () => {
    const ownProfileArtist = {
      ...ARTIST,
      isOwnProfile:    true,
      bio:             null,
      profileImageUrl: null,
      specializations: null,
      hourlyRate:      null,
      portfolioImages: [] as string[],
    };
    mockUseGetPublicArtistQuery.mockReturnValue({ data: ownProfileArtist, isLoading: false, isError: false });
    renderPage();
    expect(screen.getByText(/profile.*% complete/i)).toBeInTheDocument();
  });

  it("hides profile strength nudge when isOwnProfile is false", () => {
    renderPage();
    expect(screen.queryByText(/profile.*% complete/i)).not.toBeInTheDocument();
  });

  it("renders empty portfolio state when portfolioImages is empty", () => {
    mockUseGetPublicArtistQuery.mockReturnValue({
      data: { ...ARTIST, portfolioImages: [] },
      isLoading: false,
      isError: false,
    });
    renderPage();
    expect(screen.getByText(/no portfolio images yet/i)).toBeInTheDocument();
  });

  it("back link has aria-label containing the studio name", () => {
    renderPage();
    const backLink = screen.getByRole("link", { name: /back to ink soul/i });
    expect(backLink).toBeInTheDocument();
  });

  it("renders studio link back to /s/{studioSlug}", () => {
    renderPage();
    const studioLinks = screen.getAllByRole("link", { name: /ink soul/i });
    expect(studioLinks.some((l) => l.getAttribute("href")?.includes("/s/ink-soul"))).toBe(true);
  });

  it("shows loading skeleton while fetching", () => {
    mockUseGetPublicArtistQuery.mockReturnValue({ data: undefined, isLoading: true, isError: false });
    renderPage();
    expect(screen.getByLabelText(/loading artist profile/i)).toBeInTheDocument();
  });

  it("shows 'Artist not found' when isError is true", () => {
    mockUseGetPublicArtistQuery.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    renderPage();
    expect(screen.getByText("Artist not found.")).toBeInTheDocument();
  });
});
