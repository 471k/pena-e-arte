import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { artistsApi } from "@/features/artists/artistsApi";
import { designsApi } from "@/features/designs/designsApi";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { billingApi } from "@/features/billing/billingApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { socialApi } from "@/features/social/socialApi";
import { toast } from "sonner";
import type { ArtistResponse } from "@/features/artists/artistsApi";
import { ArtistListPage } from "@/features/artists/components/ArtistListPage";
import { ArtistDetailPage } from "@/features/artists/components/ArtistDetailPage";
import { Role } from "@/shared/types/roles";

// ── Mocks ──────────────────────────────────────────────────────────────────────

const mockUpload = vi.fn<(file: File, objectKey: string) => Promise<string | null>>();

vi.mock("@/shared/hooks/usePresignedUpload", () => ({
  usePresignedUpload: () => ({
    upload: mockUpload,
    isUploading: false,
    uploadError: null,
    clearError: () => {},
  }),
}));

// ── Seed data ──────────────────────────────────────────────────────────────────

const ELENA: ArtistResponse = {
  id: "eeee0001-0000-0000-0000-000000000001",
  studioId: "stud-0001",
  firstName: "Elena",
  lastName: "Martins",
  email: "elena.martins@ink-soul.test",
  specializations: ["traditional", "realism"],
  hourlyRate: 100,
  isActive:        true,
  avatarUrl:       null,
  portfolioImages: [],
  slug: null,
  userId:          null,
  createdAt: "2024-01-15T10:00:00.000Z",
  updatedAt: "2024-06-01T10:00:00.000Z",
};

const ARTISTS: ArtistResponse[] = [
  ELENA,
  {
    id: "eeee0002-0000-0000-0000-000000000002",
    studioId: "stud-0001",
    firstName: "Marco",
    lastName: "Silva",
    email: "marco.silva@ink-soul.test",
    specializations: ["neo-traditional"],
    hourlyRate: null,
    isActive:        true,
    avatarUrl:       null,
    portfolioImages: [],
    slug: null,
    userId:          null,
    createdAt: "2024-02-10T10:00:00.000Z",
    updatedAt: "2024-05-20T10:00:00.000Z",
  },
  {
    id: "eeee0003-0000-0000-0000-000000000003",
    studioId: "stud-0001",
    firstName: "Sara",
    lastName: "Costa",
    email: "sara.costa@ink-soul.test",
    specializations: [],
    hourlyRate: null,
    isActive:        true,
    avatarUrl:       null,
    portfolioImages: [],
    slug: null,
    userId:          null,
    createdAt: "2024-03-05T10:00:00.000Z",
    updatedAt: "2024-04-15T10:00:00.000Z",
  },
];

// ── MSW server ─────────────────────────────────────────────────────────────────

let lastPortfolioUpdateBody: { images: { imageUrl: string; style: string | null; category: string | null }[] } | null = null;

const server = setupServer(
  http.get("http://localhost/api/v1/artists", ({ request }) => {
    const search = new URL(request.url).searchParams.get("search");
    const list = search
      ? ARTISTS.filter((a) =>
          `${a.firstName} ${a.lastName}`.toLowerCase().includes(search.toLowerCase()) ||
          a.email.toLowerCase().includes(search.toLowerCase()),
        )
      : ARTISTS;
    return HttpResponse.json(list);
  }),

  http.get("http://localhost/api/v1/artists/:id", ({ params }) => {
    const artist = ARTISTS.find((a) => a.id === params.id);
    return artist
      ? HttpResponse.json(artist)
      : new HttpResponse(null, { status: 404 });
  }),

  http.put("http://localhost/api/v1/artists/:id", async ({ request, params }) => {
    const body = (await request.json()) as {
      firstName: string;
      lastName: string;
      email: string;
      specializations: string[] | null;
    };
    const artist = ARTISTS.find((a) => a.id === params.id);
    if (!artist) return new HttpResponse(null, { status: 404 });
    // Mutate in place so a subsequent GET (triggered by RTK Query's invalidatesTags
    // refetch) reflects the update too, not just this mutation's own response.
    Object.assign(artist, body, { specializations: body.specializations ?? [] });
    return HttpResponse.json(artist);
  }),

  http.delete("http://localhost/api/v1/artists/:id", () =>
    new HttpResponse(null, { status: 204 }),
  ),

  http.put("http://localhost/api/v1/artists/:id/portfolio-images", async ({ request, params }) => {
    const body = (await request.json()) as { images: { imageUrl: string; style: string | null; category: string | null }[] };
    const artist = ARTISTS.find((a) => a.id === params.id);
    if (!artist) return new HttpResponse(null, { status: 404 });
    lastPortfolioUpdateBody = body;
    return HttpResponse.json({
      ...artist,
      portfolioImages: body.images.map((img, i) => ({
        imageId:  `img-${i}`,
        imageUrl: img.imageUrl,
        style:    img.style,
        category: img.category,
      })),
    });
  }),

  http.get("http://localhost/api/v1/artists/:id/instagram/status", () =>
    HttpResponse.json({ isConnected: false, username: null, lastSyncedAt: null, postCount: 0 }),
  ),
  http.get("http://localhost/api/v1/artists/:id/social", () => HttpResponse.json([])),
  http.get("http://localhost/api/v1/designs", () => HttpResponse.json([])),
  http.get("http://localhost/api/v1/appointments", () => HttpResponse.json([])),
  http.get("http://localhost/api/v1/billing/subscription", () =>
    HttpResponse.json({ status: "Active", planName: "Starter", currentPeriodEnd: null, trialExpiresAt: null, gracePeriodEnd: null }),
  ),
  http.get("http://localhost/api/v1/studios/me", () =>
    HttpResponse.json({ id: "stud-0001", name: "Ink Soul", slug: "ink-soul", city: "Porto",
      latitude: 41.1, longitude: -8.6, showPlatformBranding: true, allowBrandingRemoval: false,
      trialExpiresAt: "2099-01-01T00:00:00Z", createdAt: "2024-01-01T00:00:00Z", isActive: true }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); lastPortfolioUpdateBody = null; cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore(role: Role = Role.Owner) {
  return configureStore({
    reducer: {
      auth:              authReducer,
      ui:                uiReducer,
      [artistsApi.reducerPath]:      artistsApi.reducer,
      [designsApi.reducerPath]:      designsApi.reducer,
      [appointmentsApi.reducerPath]: appointmentsApi.reducer,
      [billingApi.reducerPath]:      billingApi.reducer,
      [studiosApi.reducerPath]:      studiosApi.reducer,
      [socialApi.reducerPath]:       socialApi.reducer,
    },
    middleware: (gd) =>
      gd().concat(
        artistsApi.middleware,
        designsApi.middleware,
        appointmentsApi.middleware,
        billingApi.middleware,
        studiosApi.middleware,
        socialApi.middleware,
      ),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "test@ink-soul.test" }, token: "fake", tenantId: "t1", role } as any,
    },
  });
}

function renderList(role: Role = Role.Owner) {
  const store = makeStore(role);
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/artists"]}>
        <Routes>
          <Route path="/artists" element={<ArtistListPage />} />
          <Route path="/artists/:id" element={<ArtistDetailPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

function LocationProbe() {
  const { pathname, search } = useLocation();
  return <output data-testid="location">{`${pathname}${search}`}</output>;
}

function renderDetail(id: string, role: Role = Role.Owner, search = "") {
  const store = makeStore(role);
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[`/artists/${id}${search}`]}>
        <LocationProbe />
        <Routes>
          <Route path="/artists" element={<ArtistListPage />} />
          <Route path="/artists/:id" element={<ArtistDetailPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

/** Serves the seeded artist with `userId: "u1"` so the Artist-role viewer owns the profile. */
function serveOwnProfile() {
  server.use(
    http.get("http://localhost/api/v1/artists/:id", ({ params }) => {
      const artist = ARTISTS.find((a) => a.id === params.id);
      if (!artist) return new HttpResponse(null, { status: 404 });
      return HttpResponse.json({ ...artist, userId: "u1" });
    }),
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("Artists feature", () => {
  // 1. List page
  it("renders 3 ArtistCards as <Link> wrappers with ChevronRight indicators", async () => {
    renderList();

    // DataTable renders table rows (1 header + 3 data = 4 rows)
    const rows = await screen.findAllByRole("row");
    // header row + 3 data rows
    expect(rows.length).toBeGreaterThanOrEqual(4);

    // Each name appears twice — once in the mobileCard, once in the table (dual-render).
    expect(screen.getAllByText("Elena Martins")).toHaveLength(2);
    expect(screen.getAllByText("Marco Silva")).toHaveLength(2);
    expect(screen.getAllByText("Sara Costa")).toHaveLength(2);

    // Data rows have cursor-pointer class
    const dataRows = rows.slice(1);
    for (const row of dataRows) {
      expect(row).toHaveClass("cursor-pointer");
    }
  });

  it("renders a mobileCard with specializations and Edit/Delete actions for each artist (Owner role)", async () => {
    renderList();
    await screen.findAllByRole("row");

    const cardList = screen.getByRole("list");
    expect(within(cardList).getByText("Elena Martins")).toBeInTheDocument();
    expect(within(cardList).getByText("Traditional")).toBeInTheDocument();
    expect(within(cardList).getByText("Realism")).toBeInTheDocument();
    expect(within(cardList).getAllByRole("button", { name: /edit/i }).length).toBeGreaterThanOrEqual(1);
    expect(within(cardList).getAllByRole("button", { name: /delete/i }).length).toBeGreaterThanOrEqual(1);
  });

  it("clicking Delete inside a mobileCard does not also trigger row navigation", async () => {
    const user = userEvent.setup();
    renderList();
    await screen.findAllByRole("row");

    const cardList = screen.getByRole("list");
    const [deleteButton] = within(cardList).getAllByRole("button", { name: /delete/i });
    await user.click(deleteButton);

    // Row navigation did not fire — still on the list page, now showing the confirm state.
    expect(screen.getByPlaceholderText(/search by name or email/i)).toBeInTheDocument();
    expect(within(cardList).getAllByRole("button", { name: /confirm/i }).length).toBeGreaterThanOrEqual(1);
  });

  // 2. Clicking Elena navigates to detail view
  it("clicking Elena Martins card fires useGetArtistByIdQuery and renders view mode", async () => {
    const user = userEvent.setup();
    renderList();

    await screen.findAllByRole("row");

    const [, elenaTableCell] = await screen.findAllByText("Elena Martins");
    await user.click(elenaTableCell);

    // Avatar initials
    await screen.findByText("EM");

    // Name heading
    expect(screen.getByRole("heading", { name: /elena martins/i })).toBeInTheDocument();

    // Email, specializations, join date
    expect(screen.getByText(ELENA.email)).toBeInTheDocument();
    expect(screen.getByText("Traditional")).toBeInTheDocument();
    expect(screen.getByText("Realism")).toBeInTheDocument();
    expect(screen.getByText(/joined/i)).toBeInTheDocument();

    // Edit + the ⋯ overflow menu that carries Delete (Owner role)
    expect(screen.getByRole("button", { name: /edit/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /more actions/i })).toBeInTheDocument();
  });

  // 3. Edit mode
  it("edit mode: form pre-populates, empty firstName shows inline error, valid save returns to view", async () => {
    const user = userEvent.setup();
    renderDetail(ELENA.id);

    await screen.findByText("EM");

    await user.click(screen.getByRole("button", { name: /edit/i }));

    // Form pre-populated
    const firstNameInput = screen.getByLabelText(/first name/i);
    const lastNameInput = screen.getByLabelText(/last name/i);
    expect(firstNameInput).toHaveValue("Elena");
    expect(lastNameInput).toHaveValue("Martins");

    // Trigger validation error
    await user.clear(firstNameInput);
    await user.click(screen.getByRole("button", { name: /save changes/i }));
    await screen.findByText("First name is required");

    // Fix and submit successfully
    await user.type(firstNameInput, "Elena");
    await user.click(screen.getByRole("button", { name: /save changes/i }));

    // Returns to view mode: form gone, Edit button back
    await screen.findByRole("button", { name: /edit/i });
    expect(screen.queryByLabelText(/first name/i)).not.toBeInTheDocument();
  });

  it("edit mode: pre-selects the artist's specializations as toggled chips and toggling + saving persists the change", async () => {
    const user = userEvent.setup();
    renderDetail(ELENA.id);

    await screen.findByText("EM");
    await user.click(screen.getByRole("button", { name: /edit/i }));

    // Pre-populated from ELENA.specializations = ["traditional", "realism"]
    expect(screen.getByRole("checkbox", { name: "Traditional" })).toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("checkbox", { name: "Realism" })).toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("checkbox", { name: "Blackwork" })).toHaveAttribute("aria-checked", "false");

    // Toggle Blackwork on, Realism off
    await user.click(screen.getByRole("checkbox", { name: "Blackwork" }));
    await user.click(screen.getByRole("checkbox", { name: "Realism" }));
    expect(screen.getByRole("checkbox", { name: "Blackwork" })).toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("checkbox", { name: "Realism" })).toHaveAttribute("aria-checked", "false");
    await user.click(screen.getByRole("button", { name: /save changes/i }));

    await screen.findByRole("button", { name: /edit/i });
    expect(screen.getByText("Traditional")).toBeInTheDocument();
    await screen.findByText("Blackwork");
    expect(screen.queryByText("Realism")).not.toBeInTheDocument();
  });

  // 4. Confirm-delete mode
  it("confirm-delete shows warning text; Cancel returns to view mode without deleting", async () => {
    const user = userEvent.setup();
    renderDetail(ELENA.id);

    await screen.findByText("EM");

    await user.click(screen.getByRole("button", { name: /more actions/i }));
    await user.click(await screen.findByRole("menuitem", { name: /delete/i }));

    expect(await screen.findByText("Delete Elena Martins?")).toBeInTheDocument();
    expect(screen.getByText("This action cannot be undone.")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /cancel/i }));

    // Back to view mode
    expect(screen.getByRole("button", { name: /edit/i })).toBeInTheDocument();
    expect(screen.queryByText("Delete Elena Martins?")).not.toBeInTheDocument();
  });

  // 5. Breadcrumb navigation
  it("breadcrumb link navigates from detail to /artists list", async () => {
    const user = userEvent.setup();
    renderDetail(ELENA.id);

    await screen.findByText("EM");

    await user.click(screen.getByRole("link", { name: /^artists$/i }));

    // List page renders its search input immediately
    await screen.findByPlaceholderText(/search by name or email/i);
  });

  // 6. Artist role — permission gate
  it("logged in as Artist role: Edit and Delete buttons are hidden for another artist's profile", async () => {
    renderDetail(ELENA.id, Role.Artist);

    await screen.findByText("EM");

    expect(screen.queryByRole("button", { name: /edit/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /more actions/i })).not.toBeInTheDocument();
  });

  // 7. Artist role — own profile
  it("logged in as Artist role viewing own profile: Edit shows, Delete stays hidden", async () => {
    server.use(
      http.get("http://localhost/api/v1/artists/:id", ({ params }) => {
        const artist = ARTISTS.find((a) => a.id === params.id);
        if (!artist) return new HttpResponse(null, { status: 404 });
        return HttpResponse.json({ ...artist, userId: "u1" });
      }),
    );

    renderDetail(ELENA.id, Role.Artist);

    await screen.findByText("EM");

    expect(screen.getByRole("button", { name: /edit/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /more actions/i })).not.toBeInTheDocument();
  });

  // 8. Portfolio tab — style tagging
  describe("Portfolio tab style tagging", () => {
    function seedArtistWithImages() {
      server.use(
        http.get("http://localhost/api/v1/artists/:id", ({ params }) => {
          const artist = ARTISTS.find((a) => a.id === params.id);
          if (!artist) return new HttpResponse(null, { status: 404 });
          return HttpResponse.json({
            ...artist,
            portfolioImages: [
              { imageId: "img-1", imageUrl: "https://r2.example.com/tattoo1.jpg", style: null, category: null },
              { imageId: "img-2", imageUrl: "https://r2.example.com/tattoo2.jpg", style: "realism", category: "fresh" },
            ],
          });
        }),
      );
    }

    it("shows 'No style' for an untagged image and the style label for a tagged one", async () => {
      seedArtistWithImages();
      renderDetail(ELENA.id);

      await screen.findByText("EM");
      await userEvent.setup().click(screen.getByRole("tab", { name: /portfolio/i }));

      const selects = await screen.findAllByRole("combobox", { name: /tattoo style/i });
      expect(selects).toHaveLength(2);
      expect(within(selects[0]).getByText("No style")).toBeInTheDocument();
      expect(within(selects[1]).getByText("Realism")).toBeInTheDocument();
    });

    it("changing an image's style sends the full image list with that style updated", async () => {
      seedArtistWithImages();
      const user = userEvent.setup();
      renderDetail(ELENA.id);

      await screen.findByText("EM");
      await user.click(screen.getByRole("tab", { name: /portfolio/i }));

      const [firstSelect] = await screen.findAllByRole("combobox", { name: /tattoo style/i });
      await user.click(firstSelect);
      await user.click(await screen.findByRole("option", { name: "Traditional" }));

      await waitFor(() => expect(lastPortfolioUpdateBody).not.toBeNull());
      expect(lastPortfolioUpdateBody!.images).toEqual([
        { imageUrl: "https://r2.example.com/tattoo1.jpg", style: "traditional", category: null },
        { imageUrl: "https://r2.example.com/tattoo2.jpg", style: "realism", category: "fresh" },
      ]);
    });

    it("removing an image sends the remaining images without it, preserving categories", async () => {
      seedArtistWithImages();
      const user = userEvent.setup();
      renderDetail(ELENA.id);

      await screen.findByText("EM");
      await user.click(screen.getByRole("tab", { name: /portfolio/i }));

      await screen.findAllByRole("combobox", { name: /tattoo style/i });
      const [removeFirst] = screen.getAllByRole("button", { name: /remove image/i });
      await user.click(removeFirst);

      await waitFor(() => expect(lastPortfolioUpdateBody).not.toBeNull());
      expect(lastPortfolioUpdateBody!.images).toEqual([
        { imageUrl: "https://r2.example.com/tattoo2.jpg", style: "realism", category: "fresh" },
      ]);
    });

    it("shows 'Uncategorized' for an untagged image and the category label for a tagged one", async () => {
      seedArtistWithImages();
      renderDetail(ELENA.id);

      await screen.findByText("EM");
      await userEvent.setup().click(screen.getByRole("tab", { name: /portfolio/i }));

      const selects = await screen.findAllByRole("combobox", { name: /portfolio category/i });
      expect(selects).toHaveLength(2);
      expect(within(selects[0]).getByText("Uncategorized")).toBeInTheDocument();
      expect(within(selects[1]).getByText("Fresh Tattoo")).toBeInTheDocument();
    });

    it("changing an image's category sends the full image list with that category updated, style unchanged", async () => {
      seedArtistWithImages();
      const user = userEvent.setup();
      renderDetail(ELENA.id);

      await screen.findByText("EM");
      await user.click(screen.getByRole("tab", { name: /portfolio/i }));

      const [firstCategorySelect] = await screen.findAllByRole("combobox", { name: /portfolio category/i });
      await user.click(firstCategorySelect);
      await user.click(await screen.findByRole("option", { name: "Healed Tattoo" }));

      await waitFor(() => expect(lastPortfolioUpdateBody).not.toBeNull());
      expect(lastPortfolioUpdateBody!.images).toEqual([
        { imageUrl: "https://r2.example.com/tattoo1.jpg", style: null, category: "healed" },
        { imageUrl: "https://r2.example.com/tattoo2.jpg", style: "realism", category: "fresh" },
      ]);
    });

    it("'Add image' opens a dropdown with three category options; selecting one uploads and tags that category", async () => {
      seedArtistWithImages();
      mockUpload.mockResolvedValue("https://r2.example.com/new-upload.jpg");
      const user = userEvent.setup();
      renderDetail(ELENA.id);

      await screen.findByText("EM");
      await user.click(screen.getByRole("tab", { name: /portfolio/i }));

      await user.click(screen.getByRole("button", { name: /add image/i }));
      expect(screen.getByRole("menuitem", { name: "Fresh Tattoo" })).toBeInTheDocument();
      expect(screen.getByRole("menuitem", { name: "Healed Tattoo" })).toBeInTheDocument();
      expect(screen.getByRole("menuitem", { name: "Design" })).toBeInTheDocument();

      await user.click(screen.getByRole("menuitem", { name: "Design" }));

      // Selecting a category triggers a native file input click; simulate the file being chosen.
      await waitFor(() => expect(mockUpload).not.toHaveBeenCalled());
      const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement | null;
      expect(fileInput).not.toBeNull();
      const file = new File(["x"], "new.jpg", { type: "image/jpeg" });
      Object.defineProperty(fileInput, "files", { value: [file] });
      fileInput!.dispatchEvent(new Event("change"));

      await waitFor(() => expect(lastPortfolioUpdateBody).not.toBeNull());
      expect(lastPortfolioUpdateBody!.images).toContainEqual({
        imageUrl: "https://r2.example.com/new-upload.jpg", style: null, category: "design",
      });
    });

    it("a non-manager viewer sees a read-only category badge instead of the select", async () => {
      seedArtistWithImages();
      renderDetail(ELENA.id, Role.Artist);

      await screen.findByText("EM");
      await userEvent.setup().click(screen.getByRole("tab", { name: /portfolio/i }));

      expect(screen.queryAllByRole("combobox", { name: /portfolio category/i })).toHaveLength(0);
      expect(screen.getByText("Fresh Tattoo")).toBeInTheDocument();
    });
  });
});


describe("Artist profile page chrome, tabs and title", () => {
  it("renders no nested banner/sticky header and no min-h-screen root of its own", async () => {
    renderDetail(ELENA.id);
    await screen.findByText("EM");
    const container = document.body;

    expect(container.querySelector("header")).toBeNull();
    expect(container.querySelector(".sticky")).toBeNull();
    expect(container.querySelector(".min-h-screen")).toBeNull();
  });

  it("has exactly one h1 and never an h3 before an h2 on the Social tab", async () => {
    renderDetail(ELENA.id, Role.Owner, "?tab=social");
    await screen.findByText("EM");
    await screen.findByRole("heading", { name: "Connected accounts" });

    const levels = screen.getAllByRole("heading").map((h) => Number(h.tagName.slice(1)));
    expect(levels.filter((l) => l === 1)).toHaveLength(1);
    expect(levels).not.toContain(3);
    expect(levels.indexOf(2)).toBeGreaterThan(levels.indexOf(1));
  });

  it("sets the document title with 'Artists' for an owner and without it on the artist's own page", async () => {
    renderDetail(ELENA.id, Role.Owner);
    await screen.findByText("EM");
    await waitFor(() => expect(document.title).toBe("Elena Martins — Artists — TattooOS"));
    cleanup();

    serveOwnProfile();
    renderDetail(ELENA.id, Role.Artist);
    await screen.findByText("EM");
    await waitFor(() => expect(document.title).toBe("Elena Martins — TattooOS"));
  });

  it("shows a breadcrumb for an owner viewing an artist but not on the artist's own profile", async () => {
    renderDetail(ELENA.id, Role.Owner);
    await screen.findByText("EM");
    expect(screen.getByRole("navigation", { name: /breadcrumb/i })).toBeInTheDocument();
    cleanup();

    serveOwnProfile();
    renderDetail(ELENA.id, Role.Artist);
    await screen.findByText("EM");
    expect(screen.queryByRole("navigation", { name: /breadcrumb/i })).not.toBeInTheDocument();
  });

  it("labels the working-hours tab 'Availability' and no longer 'Schedule'", async () => {
    renderDetail(ELENA.id);
    await screen.findByText("EM");
    expect(screen.getByRole("tab", { name: "Availability" })).toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: "Schedule" })).not.toBeInTheDocument();
  });

  it("opens the requested tab from ?tab= and falls back to Profile for an unknown value", async () => {
    renderDetail(ELENA.id, Role.Owner, "?tab=designs");
    expect(await screen.findByText("No designs found.")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Designs" })).toHaveAttribute("aria-selected", "true");
    cleanup();

    renderDetail(ELENA.id, Role.Owner, "?tab=nonsense");
    await screen.findByText("EM");
    expect(screen.getByRole("tab", { name: "Profile" })).toHaveAttribute("aria-selected", "true");
  });

  it("writes the selected tab to the URL, replacing history and keeping other params", async () => {
    const user = userEvent.setup();
    renderDetail(ELENA.id, Role.Owner, "?keep=1");
    await screen.findByText("EM");

    await user.click(screen.getByRole("tab", { name: "Bookings" }));

    const location = screen.getByTestId("location").textContent ?? "";
    expect(location).toContain("tab=bookings");
    expect(location).toContain("keep=1");
  });

  it("OAuth return: toasts once, lands on Social and strips the one-shot params", async () => {
    const success = vi.spyOn(toast, "success").mockImplementation(() => "id");
    renderDetail(ELENA.id, Role.Owner, "?instagram=connected");

    await screen.findByText("EM");
    await waitFor(() =>
      expect(screen.getByRole("tab", { name: "Social" })).toHaveAttribute("aria-selected", "true"),
    );

    const location = screen.getByTestId("location").textContent ?? "";
    expect(location).toContain("tab=social");
    expect(location).not.toContain("instagram");
    expect(success).toHaveBeenCalledTimes(1);
    expect(success).toHaveBeenCalledWith("Instagram connected successfully!");
    success.mockRestore();
  });

  it("generic social OAuth return also strips social and platform params", async () => {
    const success = vi.spyOn(toast, "success").mockImplementation(() => "id");
    renderDetail(ELENA.id, Role.Owner, "?social=connected&platform=TikTok");

    await screen.findByText("EM");
    await waitFor(() => expect(screen.getByTestId("location").textContent).toContain("tab=social"));

    const location = screen.getByTestId("location").textContent ?? "";
    expect(location).not.toContain("social=");
    expect(location).not.toContain("platform=");
    expect(success).toHaveBeenCalledWith("TikTok connected successfully!");
    success.mockRestore();
  });

  describe("⋯ more-actions menu", () => {
    it("opens with Enter, closes with Escape and returns focus to its trigger", async () => {
      const user = userEvent.setup();
      renderDetail(ELENA.id, Role.Owner);
      await screen.findByText("EM");

      const trigger = screen.getByRole("button", { name: /more actions/i });
      trigger.focus();
      await user.keyboard("{Enter}");
      expect(await screen.findByRole("menuitem", { name: /delete/i })).toBeInTheDocument();

      await user.keyboard("{Escape}");
      await waitFor(() => expect(screen.queryByRole("menuitem", { name: /delete/i })).not.toBeInTheDocument());
      expect(trigger).toHaveFocus();
    });

    it("opens with Space", async () => {
      const user = userEvent.setup();
      renderDetail(ELENA.id, Role.Owner);
      await screen.findByText("EM");

      screen.getByRole("button", { name: /more actions/i }).focus();
      await user.keyboard(" ");
      expect(await screen.findByRole("menuitem", { name: /delete/i })).toBeInTheDocument();
    });

    it("labels the item 'Stop working as an artist' for an owner who owns the profile", async () => {
      const user = userEvent.setup();
      // Dual-role owner: an Artist-role viewer never gets the menu (canManage is owner-only),
      // so exercise the label through the Owner role on a profile whose userId matches.
      server.use(
        http.get("http://localhost/api/v1/artists/:id", ({ params }) => {
          const artist = ARTISTS.find((a) => a.id === params.id);
          return artist ? HttpResponse.json({ ...artist, userId: "u1" }) : new HttpResponse(null, { status: 404 });
        }),
      );
      renderDetail(ELENA.id, Role.Owner);
      await screen.findByText("EM");

      await user.click(screen.getByRole("button", { name: /more actions/i }));
      expect(await screen.findByRole("menuitem", { name: /stop working as an artist/i })).toBeInTheDocument();
    });
  });
});
