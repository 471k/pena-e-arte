import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";

import authReducer from "@/features/auth/authSlice";
import { DiscoverPage } from "@/features/public/components/DiscoverPage";
import type { NearbyStudioResponse } from "@/features/public/publicApi";

// ── Mocks ──────────────────────────────────────────────────────────────────────

const mockUseGetNearbyStudiosQuery = vi.fn();

vi.mock("@/features/public/publicApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/public/publicApi")>();
  return {
    ...actual,
    useGetNearbyStudiosQuery: (...args: unknown[]) => mockUseGetNearbyStudiosQuery(...args),
  };
});

// ── Seed data ──────────────────────────────────────────────────────────────────

const STUDIOS: NearbyStudioResponse[] = [
  {
    studioId:      "studio-001",
    name:          "Lisbon Ink",
    slug:          "lisbon-ink",
    city:          "Lisbon",
    coverImageUrl: null,
    distanceKm:    0.5,
    artistCount:   2,
  },
  {
    studioId:      "studio-002",
    name:          "Porto Tattoo",
    slug:          "porto-tattoo",
    city:          "Porto",
    coverImageUrl: "https://cdn.example.com/cover.jpg",
    distanceKm:    45.3,
    artistCount:   1,
  },
];

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
        <DiscoverPage />
      </MemoryRouter>
    </Provider>,
  );
}

function mockGeolocationSuccess(lat = 38.7169, lng = -9.1395) {
  const mockGetCurrentPosition = vi.fn((success: (pos: GeolocationPosition) => void) => {
    success({ coords: { latitude: lat, longitude: lng } } as GeolocationPosition);
  });
  Object.defineProperty(navigator, "geolocation", {
    value: { getCurrentPosition: mockGetCurrentPosition },
    configurable: true,
  });
}

function mockGeolocationPending() {
  const mockGetCurrentPosition = vi.fn();
  Object.defineProperty(navigator, "geolocation", {
    value: { getCurrentPosition: mockGetCurrentPosition },
    configurable: true,
  });
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("DiscoverPage", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("shows skeleton while geolocation is pending", () => {
    mockGeolocationPending();
    mockUseGetNearbyStudiosQuery.mockReturnValue({ data: undefined, isLoading: false, isFetching: false });

    renderPage();

    expect(screen.getByLabelText("Loading studios")).toBeInTheDocument();
  });

  it("renders studio cards after data loads", () => {
    mockGeolocationSuccess();
    mockUseGetNearbyStudiosQuery.mockReturnValue({ data: STUDIOS, isLoading: false, isFetching: false });

    renderPage();

    expect(screen.getByText("Lisbon Ink")).toBeInTheDocument();
    expect(screen.getByText("Porto Tattoo")).toBeInTheDocument();
  });

  it("shows empty state when no studios found", () => {
    mockGeolocationSuccess();
    mockUseGetNearbyStudiosQuery.mockReturnValue({ data: [], isLoading: false, isFetching: false });

    renderPage();

    expect(screen.getByText("No studios found nearby")).toBeInTheDocument();
  });
});
