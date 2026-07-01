import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter }   from "react-router-dom";
import { Provider }       from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer }    from "msw/node";
import authReducer        from "@/features/auth/authSlice";
import { publicApi }      from "@/features/public/publicApi";
import { savedImagesApi } from "@/features/public/savedImagesApi";
import { PortfolioFeed }  from "@/features/public/components/PortfolioFeed";
import type { PortfolioImageResponse } from "@/features/public/publicApi";

function makeStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      [publicApi.reducerPath]:      publicApi.reducer,
      [savedImagesApi.reducerPath]: savedImagesApi.reducer,
    },
    middleware: (gd) => gd().concat(publicApi.middleware, savedImagesApi.middleware),
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
    style:              "blackwork",
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
    style:              null,
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

// Three images for prev/next navigation tests
const IMAGES_NAV: PortfolioImageResponse[] = [
  { ...IMAGES[0], imageId: "img-nav-1" },
  { ...IMAGES[1], imageId: "img-nav-2" },
  { ...IMAGES[0], imageId: "img-nav-3" },
];

const server = setupServer(
  http.get("http://localhost/api/v1/public/portfolio/feed", () =>
    HttpResponse.json(IMAGES),
  ),
  // savedImagesApi — skip=true when not logged in, but handler prevents unhandled-request warnings
  http.get("http://localhost/api/v1/saved-images/ids", () =>
    HttpResponse.json([]),
  ),
  // ReviewSection queries triggered when lightbox opens
  http.get("http://localhost/api/v1/public/portfolio/:imageId/reviews", () =>
    HttpResponse.json([]),
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

  it("each tile has role=listitem", async () => {
    renderFeed();
    await screen.findByLabelText(/Tattoo by Ana Lima/i);
    const items = screen.getAllByRole("listitem");
    expect(items.length).toBeGreaterThanOrEqual(2);
  });

  it("masonry grid has role=list", async () => {
    renderFeed();
    await screen.findByLabelText(/Tattoo by Ana Lima/i);
    expect(screen.getByRole("list", { name: /portfolio images/i })).toBeInTheDocument();
  });

  it("attribution strip always shows artist name without hover (visible text)", async () => {
    renderFeed();
    await screen.findByLabelText(/Tattoo by Ana Lima/i);
    // The always-visible attribution strip contains the artist name as plain text
    expect(screen.getByText("Ana Lima")).toBeInTheDocument();
    expect(screen.getByText("Black Ink Lisbon")).toBeInTheDocument();
  });

  it("shows studio-level rating in attribution strip", async () => {
    renderFeed();
    await screen.findByLabelText(/Tattoo by Ana Lima/i);
    // Attribution strip shows averageRating (4.8) for the studio, not imageReviewCount
    expect(screen.getByText("4.8")).toBeInTheDocument();
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

  // ── Style chips ────────────────────────────────────────────────────────────

  it("style chip group has accessible label", () => {
    renderFeed();
    expect(screen.getByRole("group", { name: /filter by tattoo style/i })).toBeInTheDocument();
  });

  it("'All' chip is selected by default", () => {
    renderFeed();
    const allChip = screen.getByRole("radio", { name: /^all$/i });
    expect(allChip).toHaveAttribute("aria-checked", "true");
  });

  it("clicking a style chip deselects 'All'", async () => {
    const user = userEvent.setup();
    renderFeed();
    const blackworkChip = screen.getByRole("radio", { name: /blackwork/i });
    await user.click(blackworkChip);
    expect(blackworkChip).toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("radio", { name: /^all$/i })).toHaveAttribute("aria-checked", "false");
  });

  it("clicking a style chip sends the style param in the feed request", async () => {
    const user       = userEvent.setup();
    let capturedUrl  = "";
    server.use(
      http.get("http://localhost/api/v1/public/portfolio/feed", ({ request }) => {
        capturedUrl = request.url;
        return HttpResponse.json([]);
      }),
    );
    renderFeed();
    await screen.findByRole("group", { name: /filter by tattoo style/i });
    await user.click(screen.getByRole("radio", { name: /realism/i }));
    // Wait for the network call to fire
    await screen.findByText(/no portfolio work yet/i);
    expect(capturedUrl).toContain("style=realism");
  });

  // ── Lightbox navigation ────────────────────────────────────────────────────

  it("lightbox shows prev/next buttons when multiple images exist", async () => {
    server.use(
      http.get("http://localhost/api/v1/public/portfolio/feed", () =>
        HttpResponse.json(IMAGES_NAV),
      ),
    );
    const user = userEvent.setup();
    renderFeed();
    const tiles = await screen.findAllByRole("button", { name: /view tattoo by/i });
    // Click the middle tile so both prev and next are present
    await user.click(tiles[1]);
    expect(await screen.findByRole("button", { name: /previous image/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /next image/i })).toBeInTheDocument();
  });

  it("lightbox shows position indicator", async () => {
    server.use(
      http.get("http://localhost/api/v1/public/portfolio/feed", () =>
        HttpResponse.json(IMAGES_NAV),
      ),
    );
    const user = userEvent.setup();
    renderFeed();
    const tiles = await screen.findAllByRole("button", { name: /view tattoo by/i });
    await user.click(tiles[0]);
    expect(await screen.findByLabelText(/image 1 of 3/i)).toBeInTheDocument();
  });

  it("next button navigates to the following image", async () => {
    server.use(
      http.get("http://localhost/api/v1/public/portfolio/feed", () =>
        HttpResponse.json(IMAGES_NAV),
      ),
    );
    const user = userEvent.setup();
    renderFeed();
    const tiles = await screen.findAllByRole("button", { name: /view tattoo by/i });
    await user.click(tiles[0]);
    await screen.findByRole("dialog");
    const nextBtn = screen.getByRole("button", { name: /next image/i });
    await user.click(nextBtn);
    expect(screen.getByLabelText(/image 2 of 3/i)).toBeInTheDocument();
  });

  it("lightbox has at least one close button named 'Close'", async () => {
    const user = userEvent.setup();
    renderFeed();
    await screen.findByLabelText(/Tattoo by Ana Lima/i);
    await user.click(screen.getByLabelText(/Tattoo by Ana Lima/i));
    await screen.findByRole("dialog");
    // shadcn DialogContent renders its own close button in addition to ours,
    // so getAllByRole (not getByRole) is required to handle multiple matches.
    expect(screen.getAllByRole("button", { name: /^close$/i }).length).toBeGreaterThanOrEqual(1);
  });

  it("lightbox shows 'Book with artist' link", async () => {
    const user = userEvent.setup();
    renderFeed();
    await screen.findByLabelText(/Tattoo by Ana Lima/i);
    await user.click(screen.getByLabelText(/Tattoo by Ana Lima/i));
    expect(await screen.findByRole("link", { name: /book with ana lima/i })).toBeInTheDocument();
  });

  it("lightbox shows 'View artist profile' link", async () => {
    const user = userEvent.setup();
    renderFeed();
    await screen.findByLabelText(/Tattoo by Ana Lima/i);
    await user.click(screen.getByLabelText(/Tattoo by Ana Lima/i));
    expect(await screen.findByRole("link", { name: /view artist profile/i })).toBeInTheDocument();
  });
});

describe("PortfolioFeed — style chips accessibility", () => {
  it("all style filter chips have min-h-[44px] for WCAG 2.5.5 touch target compliance", async () => {
    renderFeed();
    await waitFor(() =>
      expect(screen.queryByLabelText("Loading portfolio")).not.toBeInTheDocument()
    );

    const chips = screen.getAllByRole("radio");
    chips.forEach((chip) => {
      expect(chip.className).toContain("min-h-[44px]");
    });
  });
});
