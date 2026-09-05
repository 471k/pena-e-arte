import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";

import authReducer from "@/features/auth/authSlice";
import { StudioPortfolioPage } from "@/features/public/components/StudioPortfolioPage";
import type { PublicStudioResponse } from "@/features/public/publicApi";

// ── Mocks ──────────────────────────────────────────────────────────────────────

const mockNavigate = vi.fn();

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useParams: () => ({ slug: "test-studio" }), useNavigate: () => mockNavigate };
});

const mockUseGetPublicStudioQuery  = vi.fn();
const mockUseGetStudioReviewsQuery = vi.fn();

vi.mock("@/features/public/publicApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/public/publicApi")>();
  return {
    ...actual,
    useGetPublicStudioQuery:       (...args: unknown[]) => mockUseGetPublicStudioQuery(...args),
    useGetStudioReviewsQuery:      (...args: unknown[]) => mockUseGetStudioReviewsQuery(...args),
    useCreateStudioReviewMutation:         () => [vi.fn(), { isLoading: false }],
    useCreateArtistReviewMutation:         () => [vi.fn(), { isLoading: false }],
    useCreatePortfolioImageReviewMutation: () => [vi.fn(), { isLoading: false }],
    useGetPortfolioImageReviewsQuery:      () => ({ data: [], isLoading: false }),
    useGetReviewableArtistAppointmentsQuery: () => ({ data: [], isLoading: false }),
    useGetReviewableStudioAppointmentsQuery: () => ({ data: [], isLoading: false }),
  };
});

// ── Seed data ──────────────────────────────────────────────────────────────────

const STUDIO: PublicStudioResponse = {
  studioId:        "studio-001",
  name:            "Ink Soul",
  slug:            "test-studio",
  city:            "Porto",
  latitude:        41.1579,
  longitude:       -8.6291,
  description:     "Premier tattoo studio in Porto.",
  coverImageUrl:   "https://cdn.example.com/cover.jpg",
  phoneNumber:     "+351 912 345 678",
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
  socialLinks: [
    { platform: "Instagram", handle: "inksoultattoo", isVerified: true, profileUrl: "https://instagram.com/inksoultattoo" },
  ],
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
    mockNavigate.mockClear();
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

  it("Book an Appointment links directly to /book when unauthenticated (guest checkout)", () => {
    renderPage(null);
    const link = screen.getByRole("link", { name: "Book an Appointment" });
    expect(link.getAttribute("href")).toMatch(/^\/book\?studio=/);
    expect(link.getAttribute("href")).not.toMatch(/\/login/);
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

  it("injects JSON-LD structured data with TattooParlor schema and aggregateRating", () => {
    renderPage();
    const script = document.head.querySelector('script[type="application/ld+json"]');
    expect(script).not.toBeNull();
    const json = JSON.parse(script!.textContent ?? "{}");
    expect(json["@type"]).toBe("TattooParlor");
    expect(json.name).toBe("Ink Soul");
    expect(json.aggregateRating).toEqual({ "@type": "AggregateRating", ratingValue: 4.7, reviewCount: 12 });
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

  it("renders 'Get Directions' link to Google Maps when coordinates are set", () => {
    renderPage();
    const link = screen.getByRole("link", { name: /get directions/i });
    expect(link).toHaveAttribute(
      "href",
      "https://www.google.com/maps/dir/?api=1&destination=41.1579%2C-8.6291",
    );
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
  });

  it("falls back to plain city text when the studio has no pinned location", () => {
    mockUseGetPublicStudioQuery.mockReturnValue({
      data: { ...STUDIO, latitude: 0, longitude: 0 },
      isLoading: false,
      isError: false,
    });
    renderPage();
    expect(screen.queryByRole("link", { name: /get directions/i })).not.toBeInTheDocument();
    // City appears in both the main content and the sidebar fallback text.
    expect(screen.getAllByText(STUDIO.city).length).toBeGreaterThan(0);
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

  it("'Browse studios' back button navigates to /discover when there's no in-app history", async () => {
    const user = userEvent.setup();
    renderPage();
    const backButton = screen.getByRole("button", { name: /back to studio discovery/i });
    await user.click(backButton);
    expect(mockNavigate).toHaveBeenCalledWith("/discover");
  });

  it("cover image renders with alt text including studio name", () => {
    renderPage();
    const img = screen.getByRole("img", { name: /Ink Soul cover/i });
    expect(img).toHaveAttribute("src", STUDIO.coverImageUrl);
  });

  describe("PublicPageHeader on StudioPortfolioPage", () => {
    it("renders 'Sign in' and 'Sign up' links when unauthenticated", () => {
      renderPage(null);
      expect(screen.getByRole("link", { name: "Sign in" })).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Sign up" })).toBeInTheDocument();
    });

    it("renders 'Register studio' link when unauthenticated", () => {
      renderPage(null);
      expect(screen.getByRole("link", { name: /register studio/i })).toBeInTheDocument();
    });

    it("renders initials avatar when authenticated", () => {
      renderPage("fake-token");
      expect(screen.getByRole("button", { name: /account menu/i })).toBeInTheDocument();
    });

    it("renders brand mark link to /discover", () => {
      renderPage();
      expect(screen.getByRole("link", { name: /tattooos.*discover/i })).toBeInTheDocument();
    });

    it("header is present in the loading skeleton", () => {
      mockUseGetPublicStudioQuery.mockReturnValue({ data: undefined, isLoading: true, isError: false });
      renderPage();
      expect(screen.getByLabelText(/loading studio page/i)).toBeInTheDocument();
    });

    it("header is present in the not-found error state", () => {
      mockUseGetPublicStudioQuery.mockReturnValue({ data: undefined, isLoading: false, isError: true });
      renderPage(null);
      expect(screen.getByText("Studio not found.")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Sign in" })).toBeInTheDocument();
    });
  });
});
