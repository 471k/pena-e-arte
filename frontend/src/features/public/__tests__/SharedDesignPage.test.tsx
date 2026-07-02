import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

import { SharedDesignPage } from "@/features/public/components/SharedDesignPage";
import type { SharedDesignResponse } from "@/features/public/publicApi";

// ── Mocks ──────────────────────────────────────────────────────────────────────

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useParams: () => ({ token: "abc123" }) };
});

const mockUseGetSharedDesignQuery = vi.fn();

vi.mock("@/features/public/publicApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/public/publicApi")>();
  return {
    ...actual,
    useGetSharedDesignQuery: (...args: unknown[]) => mockUseGetSharedDesignQuery(...args),
  };
});

// ── Seed data ──────────────────────────────────────────────────────────────────

const DESIGN: SharedDesignResponse = {
  imageUrl:   "https://cdn.example.com/design.jpg",
  title:      "Dragon Sleeve",
  studioName: "Ink Soul",
  studioSlug: "ink-soul",
  expiresAt:  "2099-12-31T00:00:00Z",
};

// ── Helpers ────────────────────────────────────────────────────────────────────

function renderPage() {
  render(
    <MemoryRouter>
      <SharedDesignPage />
    </MemoryRouter>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("SharedDesignPage", () => {
  beforeEach(() => {
    mockUseGetSharedDesignQuery.mockReturnValue({ data: DESIGN, isLoading: false, isError: false });
  });

  it("renders design image when token is valid", () => {
    renderPage();
    const img = screen.getByRole("img", { name: "Dragon Sleeve" });
    expect(img).toHaveAttribute("src", DESIGN.imageUrl);
  });

  it("renders studioName and 'Book your own tattoo' CTA", () => {
    renderPage();
    expect(screen.getByText(/Ink Soul/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /book your own tattoo/i })).toBeInTheDocument();
  });

  it("CTA links to /s/{studioSlug}", () => {
    renderPage();
    const cta = screen.getByRole("link", { name: /book your own tattoo/i });
    expect(cta.getAttribute("href")).toContain("/s/ink-soul");
  });

  it("shows expiry message when data is null (token invalid/expired)", () => {
    mockUseGetSharedDesignQuery.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    renderPage();
    expect(screen.getByText(/this link has expired/i)).toBeInTheDocument();
  });

  it("shows loading spinner while fetching", () => {
    mockUseGetSharedDesignQuery.mockReturnValue({ data: undefined, isLoading: true, isError: false });
    renderPage();
    expect(document.querySelector(".animate-spin")).toBeInTheDocument();
  });

  it("sets document title to the design title and studio name", () => {
    renderPage();
    expect(document.title).toBe("Dragon Sleeve — Shared Design by Ink Soul");
  });

  it("shows a broken-image fallback when the image fails to load", () => {
    renderPage();
    const img = screen.getByRole("img", { name: "Dragon Sleeve" });
    fireEvent.error(img);
    expect(screen.getByText(/image unavailable/i)).toBeInTheDocument();
    expect(screen.queryByRole("img", { name: "Dragon Sleeve" })).not.toBeInTheDocument();
  });
});
