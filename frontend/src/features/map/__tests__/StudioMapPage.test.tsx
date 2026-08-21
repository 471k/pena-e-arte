import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

import { StudioMapPage } from "@/features/map/components/StudioMapPage";
import type { StudioMapItem } from "@/features/studios/studiosApi";

// ── Mock react-leaflet ────────────────────────────────────────────────────────

vi.mock("react-leaflet", () => ({
  MapContainer: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="map-container">{children}</div>
  ),
  TileLayer: () => <div data-testid="tile-layer" />,
  Marker: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="marker">{children}</div>
  ),
  Popup: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="popup">{children}</div>
  ),
}));

vi.mock("leaflet", () => ({
  default: {},
  divIcon: () => ({}),
  icon:    () => ({}),
}));

// ── Mock studios API ──────────────────────────────────────────────────────────

const mockUseGetStudioMapQuery = vi.fn();

vi.mock("@/features/studios", () => ({
  useGetStudioMapQuery: (...args: unknown[]) => mockUseGetStudioMapQuery(...args),
}));

// ── Seed data ─────────────────────────────────────────────────────────────────

const STUDIOS: StudioMapItem[] = [
  { id: "s1", name: "Ink Soul",   slug: "ink-soul",   latitude: 41.15, longitude: -8.61, city: "Porto"  },
  { id: "s2", name: "Black Ink",  slug: "black-ink",  latitude: 38.72, longitude: -9.14, city: "Lisbon" },
];

// ── Helpers ───────────────────────────────────────────────────────────────────

function renderPage() {
  render(
    <MemoryRouter>
      <StudioMapPage />
    </MemoryRouter>,
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("StudioMapPage", () => {
  beforeEach(() => {
    mockUseGetStudioMapQuery.mockReturnValue({ data: STUDIOS, isLoading: false, isError: false });
  });

  it("renders the map container", () => {
    renderPage();
    expect(screen.getByTestId("map-container")).toBeInTheDocument();
  });

  it("renders a marker for each studio returned by the API", () => {
    renderPage();
    expect(screen.getAllByTestId("marker")).toHaveLength(2);
  });

  it("renders studio name in popup", () => {
    renderPage();
    expect(screen.getByText("Ink Soul")).toBeInTheDocument();
    expect(screen.getByText("Black Ink")).toBeInTheDocument();
  });

  it("shows loading indicator while isLoading is true", () => {
    mockUseGetStudioMapQuery.mockReturnValue({ data: undefined, isLoading: true, isError: false });
    renderPage();
    expect(screen.getByText(/loading studios/i)).toBeInTheDocument();
  });

  it("shows error message when isError is true", () => {
    mockUseGetStudioMapQuery.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    renderPage();
    expect(screen.getByText(/failed to load studios/i)).toBeInTheDocument();
  });

  it("shows empty message when studios array is empty", () => {
    mockUseGetStudioMapQuery.mockReturnValue({ data: [], isLoading: false, isError: false });
    renderPage();
    expect(screen.getByText(/no studios on the map yet/i)).toBeInTheDocument();
  });

  it("renders Sign in and Register links in the header", () => {
    renderPage();
    expect(screen.getByRole("link", { name: /sign in/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /register/i })).toBeInTheDocument();
  });

  it("renders a 'Get directions' link in each popup pointing to Google Maps", () => {
    renderPage();
    const links = screen.getAllByRole("link", { name: /get directions/i });
    expect(links).toHaveLength(STUDIOS.length);
    expect(links[0]).toHaveAttribute(
      "href",
      "https://www.google.com/maps/dir/?api=1&destination=41.15%2C-8.61",
    );
  });
});
