import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
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

// Brand master 1.0.0 on /map (follow-up to #475): the same lockup and swap as the other public headers.
describe("StudioMapPage brand", () => {
  beforeEach(() => {
    mockUseGetStudioMapQuery.mockReturnValue({ data: STUDIOS, isLoading: false, isError: false });
  });

  const SMALL = "/brand/after-hours/1.0.0/lockups/small-on-light.svg";
  const MICRO = "/brand/after-hours/1.0.0/marks/cartridge-micro-on-light.svg";

  it("shows the brand lockup, named TattooOS, in place of the old pen and wordmark text", () => {
    renderPage();
    const header = screen.getByRole("banner");
    // jsdom applies no Tailwind, so both tones of the swap are in the tree; a real browser displays one.
    const imgs = within(header).getAllByRole("img", { name: "TattooOS" });
    expect(imgs.map((i) => i.getAttribute("src"))).toEqual([SMALL, MICRO]);
    expect(header.querySelector("svg.lucide-pen-line")).toBeNull();
    expect(header.querySelector(".font-wordmark")).toBeNull();
    expect(within(header).queryByText("TattooOS")).not.toBeInTheDocument();
  });

  it("wide (360px and up) shows the small lockup; narrow (below 360px) the micro mark", () => {
    renderPage();
    const header = screen.getByRole("banner");
    const full = within(header).getAllByRole("img", { name: "TattooOS" })[0].closest("picture")!.parentElement!;
    const micro = within(header).getAllByRole("img", { name: "TattooOS" })[1].closest("picture")!.parentElement!;
    expect(full.className).toBe("inline-flex max-[359px]:hidden");
    expect(micro.className).toBe("hidden max-[359px]:inline-flex");
  });

  it("keeps the header's links, destinations and names as they were; the brand is not a link", () => {
    renderPage();
    const header = screen.getByRole("banner");
    const links = within(header).getAllByRole("link");
    expect(links.map((l) => [l.textContent, l.getAttribute("href"), l.getAttribute("aria-label")])).toEqual([
      ["List view", "/discover", null],
      ["Sign in", "/login", null],
      ["Register your studio", "/register", null],
    ]);
    for (const img of within(header).getAllByRole("img", { name: "TattooOS" })) expect(img.closest("a")).toBeNull();
  });
});
