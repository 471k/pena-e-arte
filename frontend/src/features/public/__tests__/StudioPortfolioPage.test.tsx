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

const mockUseGetPublicStudioQuery  = vi.fn();
const mockUseGetStudioReviewsQuery = vi.fn();

vi.mock("@/features/public/publicApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/public/publicApi")>();
  return {
    ...actual,
    useGetPublicStudioQuery:       (...args: unknown[]) => mockUseGetPublicStudioQuery(...args),
    useGetStudioReviewsQuery:      (...args: unknown[]) => mockUseGetStudioReviewsQuery(...args),
    useCreateStudioReviewMutation: () => [vi.fn(), { isLoading: false }],
    useCreateArtistReviewMutation: () => [vi.fn(), { isLoading: false }],
  };
});

// ── Seed data ──────────────────────────────────────────────────────────────────

const STUDIO: PublicStudioResponse = {
  studioId:        "studio-001",
  name:            "Ink Soul",
  slug:            "test-studio",
  city:            "Porto",
  description:     "Premier tattoo studio in Porto.",
  coverImageUrl:   "https://cdn.example.com/cover.jpg",
  phoneNumber:     "+351 912 345 678",
  instagramHandle: "inksoultattoo",
  averageRating:   4.7,
  reviewCount:     12,
  galleryImages:   [
    "https://cdn.example.com/art1.jpg",
    "https://cdn.example.com/art2.jpg",
    "https://cdn.example.com/art3.jpg",
  ],
  artists: [
    {
      artistId:        "artist-001",
      name:            "Maria Silva",
      slug:            "maria-silva",
      bio:             "Specialises in neo-trad.",
      profileImageUrl: null,
      specializations: "Neo-Traditional, Illustrative",
      averageRating:   4.9,
      reviewCount:     8,
    },
    {
      artistId:        "artist-002",
      name:            "João Costa",
      slug:            "joao-costa",
      bio:             null,
      profileImageUrl: null,
      specializations: null,
      averageRating:   null,
      reviewCount:     0,
    },
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
    mockUseGetStudioReviewsQuery.mockReturnValue({ data: [], isLoading: false });
  });

  it("renders studio name and city when data loads", () => {
    renderPage();
    expect(screen.getByText("Ink Soul")).toBeInTheDocument();
    // City appears in main content and sidebar
    expect(screen.getAllByText("Porto").length).toBeGreaterThan(0);
  });

  it("renders cover image when coverImageUrl is present", () => {
    renderPage();
    const img = screen.getByRole("img", { name: /Ink Soul cover/i });
    expect(img).toHaveAttribute("src", STUDIO.coverImageUrl);
  });

  it("renders artist cards for each artist in the list", () => {
    renderPage();
    expect(screen.getByText("Maria Silva")).toBeInTheDocument();
    expect(screen.getByText("João Costa")).toBeInTheDocument();
  });

  it("shows loading skeleton while fetching", () => {
    mockUseGetPublicStudioQuery.mockReturnValue({ data: undefined, isLoading: true, isError: false });
    renderPage();
    expect(screen.getByLabelText(/loading studio page/i)).toBeInTheDocument();
  });

  it("shows 'Studio not found' when isError is true", () => {
    mockUseGetPublicStudioQuery.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    renderPage();
    expect(screen.getByText("Studio not found.")).toBeInTheDocument();
  });

  it("renders 'Book an Appointment' CTA when showBookingCta is true", () => {
    renderPage();
    expect(screen.getByRole("link", { name: "Book an Appointment" })).toBeInTheDocument();
  });

  it("Book an Appointment links to /login redirect when unauthenticated", () => {
    renderPage(null);
    const link = screen.getByRole("link", { name: "Book an Appointment" });
    expect(link.getAttribute("href")).toMatch(/\/login/);
  });

  it("Book an Appointment links directly to /book when authenticated", () => {
    renderPage("fake-token");
    const link = screen.getByRole("link", { name: "Book an Appointment" });
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

  // ── New tests ────────────────────────────────────────────────────────────────

  it("renders studio rating when reviewCount > 0", () => {
    renderPage();
    expect(screen.getByText(/4\.7/)).toBeInTheDocument();
    expect(screen.getByText(/12 reviews/)).toBeInTheDocument();
  });

  it("renders phone number link when phoneNumber is set", () => {
    renderPage();
    const phoneLink = screen.getByRole("link", { name: /call ink soul/i });
    expect(phoneLink).toHaveAttribute("href", "tel:+351 912 345 678");
  });

  it("renders Instagram link when instagramHandle is set", () => {
    renderPage();
    const igLink = screen.getByRole("link", { name: /instagram/i });
    expect(igLink).toHaveAttribute("href", "https://instagram.com/inksoultattoo");
  });

  it("renders gallery images when galleryImages is not empty", () => {
    renderPage();
    const galleryButtons = screen.getAllByRole("button", { name: /view portfolio image/i });
    expect(galleryButtons).toHaveLength(3);
  });

  it("gallery section is hidden when galleryImages is empty", () => {
    mockUseGetPublicStudioQuery.mockReturnValue({
      data: { ...STUDIO, galleryImages: [] },
      isLoading: false,
      isError: false,
    });
    renderPage();
    expect(screen.queryByRole("button", { name: /view portfolio image/i })).not.toBeInTheDocument();
  });

  it("artist cards include ChevronRight affordance via aria-label on the Link", () => {
    renderPage();
    expect(screen.getByRole("link", { name: "View Maria Silva's portfolio" })).toBeInTheDocument();
  });

  it("renders artist specialization under artist name", () => {
    renderPage();
    expect(screen.getByText("Neo-Traditional")).toBeInTheDocument();
  });

  it("'Browse studios' back link points to /discover", () => {
    renderPage();
    const backLink = screen.getByRole("link", { name: /back to studio discovery/i });
    expect(backLink).toHaveAttribute("href", "/discover");
  });

  it("cover image renders with alt text including studio name", () => {
    renderPage();
    const img = screen.getByRole("img", { name: /Ink Soul cover/i });
    expect(img).toHaveAttribute("src", STUDIO.coverImageUrl);
  });
});
