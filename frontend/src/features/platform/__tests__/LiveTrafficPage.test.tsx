import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { platformApi } from "@/features/platform/platformApi";
import { LiveTrafficPage } from "@/features/platform/components/LiveTrafficPage";
import type {
  LiveTrafficSnapshotResponse,
  TrafficHistoryResponse,
  TrafficBreakdownResponse,
} from "@/features/platform/platform.types";

// react-leaflet/leaflet mocks — mirrors StudioMapPage.test.tsx's established pattern (jsdom has
// no real tile/canvas rendering, so the map itself is stubbed to plain divs with test ids).
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

// SignalR mock — mirrors useSignalR.test.tsx's established pattern. LiveTrafficPage mounts
// useLiveTrafficHub(true), which would otherwise attempt a real connection in jsdom.
vi.mock("@microsoft/signalr", () => {
  function HubConnectionBuilder(this: Record<string, unknown>) {
    this.withUrl                = vi.fn().mockReturnValue(this);
    this.withAutomaticReconnect = vi.fn().mockReturnValue(this);
    this.configureLogging       = vi.fn().mockReturnValue(this);
    this.build                  = vi.fn(() => ({
      on:     vi.fn(),
      start:  vi.fn().mockResolvedValue(undefined),
      invoke: vi.fn().mockResolvedValue(undefined),
      stop:   vi.fn().mockResolvedValue(undefined),
    }));
  }
  return { HubConnectionBuilder, LogLevel: { Warning: 2 } };
});

const SNAPSHOT: LiveTrafficSnapshotResponse = {
  totalActive: 2,
  guestCount:  1,
  roleCounts:  { owner: 1 },
  visitors: [
    {
      visitorId: "v1", role: "owner", studioId: "s1", studioName: "Ink Society",
      countryCode: "AL", city: "Tirana", latitude: 41.3275, longitude: 19.8187,
      deviceType: "desktop", browser: "Chrome",
      path: "/dashboard", connectedAt: new Date().toISOString(),
    },
    {
      // No resolved coordinates (GeoIP miss / private IP) — must still appear in the table
      // but must not produce a map marker.
      visitorId: "v2", role: null, studioId: null, studioName: null,
      countryCode: "GR", city: "Athens", latitude: null, longitude: null,
      deviceType: "mobile", browser: "Safari",
      path: "/discover", connectedAt: new Date().toISOString(),
    },
  ],
};

const HISTORY: TrafficHistoryResponse = {
  days: 30,
  dataPoints: [
    { date: "2026-08-01", guestCount: 5, clientCount: 2, artistCount: 1, ownerCount: 0, issuerCount: 0 },
    { date: "2026-08-02", guestCount: 7, clientCount: 3, artistCount: 1, ownerCount: 1, issuerCount: 0 },
  ],
};

const BREAKDOWN: TrafficBreakdownResponse = {
  days: 30,
  topCountries:     [{ countryCode: "AL", country: null, count: 10 }],
  deviceBreakdown:  [{ name: "desktop", count: 6 }, { name: "mobile", count: 4 }],
  browserBreakdown: [{ name: "Chrome", count: 8 }],
  topPages:         [{ name: "/discover", count: 12 }],
  topNetworks:      [{ name: "Example ISP", count: 7 }],
};

const server = setupServer(
  http.get("http://localhost/api/v1/platform/traffic/live", () => HttpResponse.json(SNAPSHOT)),
  http.get("http://localhost/api/v1/platform/traffic/history", () => HttpResponse.json(HISTORY)),
  http.get("http://localhost/api/v1/platform/traffic/breakdown", () => HttpResponse.json(BREAKDOWN)),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth:                      authReducer,
      [platformApi.reducerPath]: platformApi.reducer,
    },
    middleware: (gd) => gd().concat(platformApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "issuer@platform.test" }, token: "fake", tenantId: null, role: "issuer" } as any,
    },
  });
}

function renderPage() {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter>
        <LiveTrafficPage />
      </MemoryRouter>
    </Provider>,
  );
}

describe("LiveTrafficPage", () => {
  it("renders the header", () => {
    renderPage();
    expect(screen.getByText(/live traffic/i)).toBeInTheDocument();
  });

  it("renders KPI counts once loaded", async () => {
    renderPage();

    expect(await screen.findByText("2")).toBeInTheDocument(); // Active now
    await waitFor(() => expect(screen.getAllByText("1").length).toBeGreaterThan(0)); // Guests / Owners
  });

  it("renders the live visitor table with role, location, and page", async () => {
    renderPage();

    expect(await screen.findByText("Ink Society")).toBeInTheDocument();
    expect(screen.getByText("Tirana")).toBeInTheDocument();
    expect(screen.getByText("/dashboard")).toBeInTheDocument();
    expect(screen.getByText("Guest")).toBeInTheDocument();
  });

  it("shows the empty state when no one is on the site", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/traffic/live", () =>
        HttpResponse.json({ totalActive: 0, guestCount: 0, roleCounts: {}, visitors: [] }),
      ),
    );
    renderPage();

    expect(await screen.findByText(/no one's on the site right now/i)).toBeInTheDocument();
  });

  it("shows an error message when the live snapshot request fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/traffic/live", () =>
        HttpResponse.json({ message: "fail" }, { status: 500 }),
      ),
    );
    renderPage();

    await waitFor(() => expect(screen.getByText(/failed to load live traffic/i)).toBeInTheDocument());
  });

  it("renders the breakdown lists (top countries, device/browser, top pages, top networks)", async () => {
    renderPage();

    expect(await screen.findByText("Chrome")).toBeInTheDocument();
    expect(screen.getAllByText("/discover").length).toBeGreaterThan(0);
    expect(screen.getByText("Example ISP")).toBeInTheDocument();
  });

  it("renders one map marker per visitor with resolved coordinates, omitting those without", async () => {
    renderPage();

    await screen.findByText("Ink Society");
    // v1 has latitude/longitude, v2 does not — only one marker expected.
    expect(screen.getAllByTestId("marker")).toHaveLength(1);
  });

  it("shows the 'no located visitors' message when nobody has resolved coordinates", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/traffic/live", () =>
        HttpResponse.json({
          totalActive: 1,
          guestCount: 1,
          roleCounts: {},
          visitors: [
            {
              visitorId: "v3", role: null, studioId: null, studioName: null,
              countryCode: null, city: null, latitude: null, longitude: null,
              deviceType: null, browser: null, path: "/discover", connectedAt: new Date().toISOString(),
            },
          ],
        }),
      ),
    );
    renderPage();

    expect(await screen.findByText(/no located visitors right now/i)).toBeInTheDocument();
    expect(screen.queryByTestId("marker")).not.toBeInTheDocument();
  });
});
