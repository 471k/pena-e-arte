import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";

import authReducer from "@/features/auth/authSlice";
import { ArtistPortfolioPage } from "@/features/public/components/ArtistPortfolioPage";
import type { PublicArtistResponse, ArtistPortfolioImage } from "@/features/public/publicApi";

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
    useCreateStudioReviewMutation:         () => [vi.fn(), { isLoading: false }],
    useCreateArtistReviewMutation:         () => [vi.fn(), { isLoading: false }],
    useCreatePortfolioImageReviewMutation: () => [vi.fn(), { isLoading: false }],
    useGetPortfolioImageReviewsQuery:      () => ({ data: [], isLoading: false }),
    useGetReviewableArtistAppointmentsQuery: () => ({ data: [], isLoading: false }),
    useGetReviewableStudioAppointmentsQuery: () => ({ data: [], isLoading: false }),
    useRecordArtistViewMutation:           () => [vi.fn(), { isLoading: false }],
    useGetArtistInstagramPostsQuery:       () => ({ data: [], isLoading: false }),
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
    { imageId: "img-001", imageUrl: "https://cdn.example.com/port1.jpg", style: null, category: null },
    { imageId: "img-002", imageUrl: "https://cdn.example.com/port2.jpg", style: null, category: null },
  ] satisfies ArtistPortfolioImage[],
  specializations: "Blackwork, Neo-Trad",
  hourlyRate:      120,
  averageRating:   4.5,
  reviewCount:     8,
  studioName:      "Ink Soul",
  studioSlug:      "ink-soul",
  showBookingCta:  true,
  isOwnProfile:    false,
  socialLinks: [
    { platform: "Instagram", handle: "mariasilva.ink", isVerified: true, profileUrl: "https://instagram.com/mariasilva.ink" },
  ],
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

  it("sets canonical link to /artist/{slug}, matching the router path", () => {
    renderPage();
    const canonical = document.head.querySelector('link[rel="canonical"]');
    expect(canonical?.getAttribute("href")).toBe("https://tattooos.co/artist/maria-silva");
  });

  it("injects JSON-LD structured data with Person schema for the artist", () => {
    renderPage();
    const script = document.head.querySelector('script[type="application/ld+json"]');
    expect(script).not.toBeNull();
    const json = JSON.parse(script!.textContent ?? "{}");
    expect(json["@type"]).toBe("Person");
    expect(json.name).toBe("Maria Silva");
    expect(json.url).toBe("https://tattooos.co/artist/maria-silva");
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

  // Guest checkout (Decision #1/#13, 2026-08-31): /book itself branches on auth state, so
  // the CTA must link straight there for both authenticated and unauthenticated visitors —
  // never through a forced /login hop. This page's CTA was missed when the guest checkout
  // feature shipped and still forced login; found via manual browser verification, 2026-09-02.
  it("book button links directly to booking when not authenticated (guest checkout)", () => {
    renderPage(null);
    const bookLink = screen.getByRole("link", { name: /book an appointment/i });
    expect(bookLink.getAttribute("href")).toMatch(/^\/book\?studio=/);
    expect(bookLink.getAttribute("href")).not.toContain("/login");
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
      portfolioImages: [] as ArtistPortfolioImage[],
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

  it("back button has aria-label containing the studio name", () => {
    renderPage();
    const backButton = screen.getByRole("button", { name: /back to ink soul/i });
    expect(backButton).toBeInTheDocument();
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

  describe("PublicPageHeader on ArtistPortfolioPage", () => {
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
      mockUseGetPublicArtistQuery.mockReturnValue({ data: undefined, isLoading: true, isError: false });
      renderPage();
      expect(screen.getByLabelText(/loading artist profile/i)).toBeInTheDocument();
    });

    it("header is present in the not-found error state", () => {
      mockUseGetPublicArtistQuery.mockReturnValue({ data: undefined, isLoading: false, isError: true });
      renderPage(null);
      expect(screen.getByText("Artist not found.")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Sign in" })).toBeInTheDocument();
    });
  });

  describe("category filtering", () => {
    it("does not render category tabs when fewer than 2 distinct categories are present", () => {
      renderPage();
      expect(screen.queryByRole("group", { name: /filter by portfolio category/i })).not.toBeInTheDocument();
    });

    it("renders category tabs and filters images when >= 2 distinct categories are present", () => {
      mockUseGetPublicArtistQuery.mockReturnValue({
        data: {
          ...ARTIST,
          portfolioImages: [
            { imageId: "img-fresh", imageUrl: "https://cdn.example.com/fresh.jpg", style: null, category: "fresh" },
            { imageId: "img-design", imageUrl: "https://cdn.example.com/design.jpg", style: null, category: "design" },
          ] satisfies ArtistPortfolioImage[],
        },
        isLoading: false,
        isError: false,
      });
      renderPage();

      const group = screen.getByRole("group", { name: /filter by portfolio category/i });
      expect(group).toBeInTheDocument();

      expect(screen.getAllByRole("button", { name: /view portfolio image/i })).toHaveLength(2);

      fireEvent.click(screen.getByRole("radio", { name: "Designs" }));
      expect(screen.getAllByRole("button", { name: /view portfolio image/i })).toHaveLength(1);
    });

    it("combines an active category with an active style filter", () => {
      mockUseGetPublicArtistQuery.mockReturnValue({
        data: {
          ...ARTIST,
          portfolioImages: [
            { imageId: "img-1", imageUrl: "https://cdn.example.com/1.jpg", style: "blackwork", category: "fresh" },
            { imageId: "img-2", imageUrl: "https://cdn.example.com/2.jpg", style: "realism", category: "fresh" },
            { imageId: "img-3", imageUrl: "https://cdn.example.com/3.jpg", style: "blackwork", category: "design" },
          ] satisfies ArtistPortfolioImage[],
        },
        isLoading: false,
        isError: false,
      });
      renderPage();

      fireEvent.click(screen.getByRole("radio", { name: "Fresh Tattoos" }));
      fireEvent.click(screen.getByRole("radio", { name: "Blackwork" }));

      expect(screen.getAllByRole("button", { name: /view portfolio image/i })).toHaveLength(1);
    });
  });
});
