import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter }   from "react-router-dom";
import { Provider }       from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer }    from "msw/node";
import authReducer        from "@/features/auth/authSlice";
import { publicApi }      from "@/features/public/publicApi";
import { PortfolioFeed }  from "@/features/public/components/PortfolioFeed";
import type { PortfolioImageResponse } from "@/features/public/publicApi";

function makeStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      [publicApi.reducerPath]: publicApi.reducer,
    },
    middleware: (gd) => gd().concat(publicApi.middleware),
  });
}

function renderFeed(props: Partial<React.ComponentProps<typeof PortfolioFeed>> = {}) {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter>
        <PortfolioFeed lat={null} lng={null} radiusKm={50} nearOnly={false} {...props} />
      </MemoryRouter>
    </Provider>,
  );
}

const IMAGES: PortfolioImageResponse[] = [
  {
    imageId:            "img-001",
    imageUrl:           "https://example.com/tattoo1.jpg",
    artistName:         "Ana Lima",
    artistSlug:         "ana-lima",
    studioName:         "Black Ink Lisbon",
    studioSlug:         "black-ink-lisbon",
    averageRating:      4.8,
    reviewCount:        22,
    imageAverageRating: 4.5,
    imageReviewCount:   5,
    distanceKm:         null,
    viewCount:          150,
  },
  {
    imageId:            "img-002",
    imageUrl:           "https://example.com/tattoo2.jpg",
    artistName:         "João Costa",
    artistSlug:         "joao-costa",
    studioName:         "Dark Arts Porto",
    studioSlug:         "dark-arts-porto",
    averageRating:      null,
    reviewCount:        0,
    imageAverageRating: null,
    imageReviewCount:   0,
    distanceKm:         3.2,
    viewCount:          40,
  },
];

const server = setupServer(
  http.get("http://localhost/api/v1/public/portfolio/feed", () =>
    HttpResponse.json(IMAGES),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "warn" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

describe("PortfolioFeed", () => {
  it("renders artist name in each tile overlay area (accessible via aria-label)", async () => {
    renderFeed();
    expect(await screen.findByLabelText(/Tattoo by Ana Lima/i)).toBeInTheDocument();
    expect(await screen.findByLabelText(/Tattoo by João Costa/i)).toBeInTheDocument();
  });

  it("shows distance badge when distanceKm is not null", async () => {
    renderFeed();
    await screen.findByLabelText(/Tattoo by João Costa/i);
    expect(screen.getByText(/3\.2/)).toBeInTheDocument();
  });

  it("does NOT show distance badge when distanceKm is null", async () => {
    renderFeed();
    await screen.findByLabelText(/Tattoo by Ana Lima/i);
    const anaTile = screen.getByLabelText(/Tattoo by Ana Lima/i);
    expect(anaTile.querySelector("span")?.textContent).not.toMatch(/km/);
  });

  it("shows rating and review count when reviewCount > 0", async () => {
    renderFeed();
    await screen.findByLabelText(/Tattoo by Ana Lima/i);
    expect(screen.getByText("(22)")).toBeInTheDocument();
  });

  it("does NOT show rating row when reviewCount is 0", async () => {
    renderFeed();
    await screen.findByLabelText(/Tattoo by João Costa/i);
    expect(screen.queryByText("(0)")).not.toBeInTheDocument();
  });

  it("shows empty state with register link when feed is empty", async () => {
    server.use(
      http.get("http://localhost/api/v1/public/portfolio/feed", () =>
        HttpResponse.json([]),
      ),
    );
    renderFeed();
    expect(await screen.findByText(/no portfolio work yet/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /register your studio/i })).toBeInTheDocument();
  });

  it("shows empty state for nearOnly with specific message", async () => {
    server.use(
      http.get("http://localhost/api/v1/public/portfolio/feed", () =>
        HttpResponse.json([]),
      ),
    );
    renderFeed({ nearOnly: true, lat: 38.7, lng: -9.1 });
    expect(await screen.findByText(/no artists with portfolio images found nearby/i))
      .toBeInTheDocument();
  });

  it("each tile is a button (opens lightbox, not a direct link)", async () => {
    renderFeed();
    const tile = await screen.findByLabelText(/Tattoo by Ana Lima/i);
    expect(tile.tagName).toBe("BUTTON");
  });

  it("'Load more' button is hidden when fewer than 24 images returned", async () => {
    renderFeed();
    await screen.findByLabelText(/Tattoo by Ana Lima/i);
    expect(screen.queryByRole("button", { name: /load more/i })).not.toBeInTheDocument();
  });

  it("'Load more' button appears when 24 or more images returned", async () => {
    const manyImages: PortfolioImageResponse[] = Array.from({ length: 24 }, (_, i) => ({
      ...IMAGES[0],
      imageId:   `img-${i}`,
      imageUrl:   `https://example.com/tattoo${i}.jpg`,
      artistSlug: `artist-${i}`,
    }));
    server.use(
      http.get("http://localhost/api/v1/public/portfolio/feed", () =>
        HttpResponse.json(manyImages),
      ),
    );
    renderFeed();
    expect(await screen.findByRole("button", { name: /load more/i })).toBeInTheDocument();
  });

  it("clicking 'Load more' loads page 2 content", async () => {
    const user = userEvent.setup();
    const page1: PortfolioImageResponse[] = Array.from({ length: 24 }, (_, i) => ({
      ...IMAGES[0],
      imageId:    `p1-img-${i}`,
      imageUrl:   `https://example.com/p1-tattoo${i}.jpg`,
      artistSlug: `p1-artist-${i}`,
      studioName: "Page One Studio",
    }));
    const page2: PortfolioImageResponse[] = [
      {
        ...IMAGES[1],
        imageId:   "p2-img-0",
        imageUrl:   "https://example.com/p2-unique.jpg",
        artistSlug: "p2-artist",
        studioName: "Page Two Studio",
      },
    ];
    server.use(
      http.get("http://localhost/api/v1/public/portfolio/feed", ({ request }) => {
        const url = new URL(request.url);
        return HttpResponse.json(url.searchParams.get("page") === "2" ? page2 : page1);
      }),
    );
    renderFeed();
    const loadMore = await screen.findByRole("button", { name: /load more/i });
    await user.click(loadMore);
    expect(await screen.findByLabelText(/Tattoo by João Costa at Page Two Studio/i))
      .toBeInTheDocument();
  });
});
