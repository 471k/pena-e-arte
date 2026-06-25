import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

import { EmbedPage } from "@/features/public/components/EmbedPage";
import type { PublicStudioResponse } from "@/features/public/publicApi";

// ── Mocks ──────────────────────────────────────────────────────────────────────

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useParams: () => ({ studioSlug: "tinta-alma" }) };
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
  name:          "Tinta & Alma",
  slug:          "tinta-alma",
  city:          "Porto",
  description:   "Premier tattoo studio in Porto.",
  coverImageUrl: null,
  artists: [
    { artistId: "artist-001", name: "Rafaela Costa", slug: "rafaela-costa", bio: null },
    { artistId: "artist-002", name: "João Dias",     slug: "joao-dias",     bio: "Neo-trad specialist." },
  ],
  showBookingCta: true,
};

// ── Helpers ────────────────────────────────────────────────────────────────────

function renderPage() {
  render(
    <MemoryRouter>
      <EmbedPage />
    </MemoryRouter>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("EmbedPage", () => {
  beforeEach(() => {
    mockUseGetPublicStudioQuery.mockReturnValue({ data: STUDIO, isLoading: false, isError: false });
  });

  it("shows skeleton while loading", () => {
    mockUseGetPublicStudioQuery.mockReturnValue({ data: undefined, isLoading: true, isError: false });
    renderPage();
    expect(screen.getByLabelText("Loading booking widget")).toBeInTheDocument();
  });

  it("shows error state when studio not found", () => {
    mockUseGetPublicStudioQuery.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    renderPage();
    expect(screen.getByText("Studio not found.")).toBeInTheDocument();
  });

  it("renders studio name, book button, and artist list", () => {
    renderPage();
    expect(screen.getByText("Tinta & Alma")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Book an Appointment/i })).toBeInTheDocument();
    expect(screen.getByText("Rafaela Costa")).toBeInTheDocument();
    expect(screen.getByText("João Dias")).toBeInTheDocument();
  });
});
