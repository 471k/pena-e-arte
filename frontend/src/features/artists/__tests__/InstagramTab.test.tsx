import { describe, it, expect, beforeAll, afterEach, afterAll, vi } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { artistsApi } from "@/features/artists/artistsApi";
import type {
  InstagramConnectionStatus,
  InstagramPostItem,
  ConnectInstagramResponse,
} from "@/features/artists/artistsApi";
import { InstagramTab } from "@/features/artists/components/InstagramTab";

const ARTIST_ID = "artist-0001";

const DISCONNECTED_STATUS: InstagramConnectionStatus = {
  isConnected:  false,
  username:     null,
  lastSyncedAt: null,
  postCount:    0,
};

const CONNECTED_STATUS: InstagramConnectionStatus = {
  isConnected:  true,
  username:     "ink_artist",
  lastSyncedAt: "2026-07-01T10:00:00Z",
  postCount:    3,
};

const POSTS: InstagramPostItem[] = [
  { id: "post-1", instagramMediaId: "m1", mediaUrl: "https://img/1.jpg", thumbnailUrl: null, caption: "a", mediaType: "IMAGE", postedAt: "2026-07-01T09:00:00Z", isVisible: true },
  { id: "post-2", instagramMediaId: "m2", mediaUrl: "https://img/2.jpg", thumbnailUrl: null, caption: "b", mediaType: "IMAGE", postedAt: "2026-06-30T09:00:00Z", isVisible: true },
  { id: "post-3", instagramMediaId: "m3", mediaUrl: "https://img/3.jpg", thumbnailUrl: null, caption: "c", mediaType: "IMAGE", postedAt: "2026-06-29T09:00:00Z", isVisible: false },
];

let disconnectCalled = false;
let toggledPostId: string | null = null;
let toggledVisibility: boolean | null = null;

const server = setupServer(
  http.get(`http://localhost/api/v1/artists/${ARTIST_ID}/instagram/status`, () =>
    HttpResponse.json(DISCONNECTED_STATUS),
  ),
  http.get(`http://localhost/api/v1/artists/${ARTIST_ID}/instagram/connect-url`, () =>
    HttpResponse.json({ authUrl: "https://api.instagram.com/oauth/authorize?x=1" } satisfies ConnectInstagramResponse),
  ),
  http.get(`http://localhost/api/v1/artists/${ARTIST_ID}/instagram/posts`, () =>
    HttpResponse.json(POSTS),
  ),
  http.put(`http://localhost/api/v1/artists/${ARTIST_ID}/instagram/posts/:postId/visibility`, async ({ params, request }) => {
    toggledPostId = params.postId as string;
    const body = (await request.json()) as { isVisible: boolean };
    toggledVisibility = body.isVisible;
    return new HttpResponse(null, { status: 204 });
  }),
  http.delete(`http://localhost/api/v1/artists/${ARTIST_ID}/instagram/disconnect`, () => {
    disconnectCalled = true;
    return new HttpResponse(null, { status: 204 });
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  server.resetHandlers();
  disconnectCalled = false;
  toggledPostId = null;
  toggledVisibility = null;
  cleanup();
});
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      ui:   uiReducer,
      [artistsApi.reducerPath]: artistsApi.reducer,
    },
    middleware: (gd) => gd().concat(artistsApi.middleware),
    preloadedState: {
      auth: {
        user: { id: "u1", email: "owner@ink.test" },
        token: "fake-token",
        tenantId: "stud-0001",
        role: "owner",
        pendingReferralCode: null,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any,
      ui: { readOnlyError: null, sessionExpired: false, studioSuspended: false },
    },
  });
}

function renderTab(canConnect = true, canManagePosts = true) {
  const store = makeStore();
  render(
    <Provider store={store}>
      <InstagramTab artistId={ARTIST_ID} canConnect={canConnect} canManagePosts={canManagePosts} />
    </Provider>,
  );
  return store;
}

describe("InstagramTab", () => {
  it("shows the Connect Instagram button and descriptive text when disconnected", async () => {
    renderTab();
    expect(await screen.findByRole("button", { name: /connect instagram/i })).toBeInTheDocument();
    expect(screen.getByText(/automatically sync their posts/i)).toBeInTheDocument();
  });

  it("shows username, post count badge, and Disconnect button when connected", async () => {
    server.use(
      http.get(`http://localhost/api/v1/artists/${ARTIST_ID}/instagram/status`, () =>
        HttpResponse.json(CONNECTED_STATUS),
      ),
    );
    renderTab();

    expect(await screen.findByText("@ink_artist")).toBeInTheDocument();
    expect(screen.getByText("3 posts")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /disconnect/i })).toBeInTheDocument();
  });

  it("renders one <img> per synced post", async () => {
    server.use(
      http.get(`http://localhost/api/v1/artists/${ARTIST_ID}/instagram/status`, () =>
        HttpResponse.json(CONNECTED_STATUS),
      ),
    );
    renderTab();

    await screen.findByText("@ink_artist");
    await waitFor(() => {
      expect(screen.getAllByRole("img")).toHaveLength(3);
    });
  });

  it("clicking the visibility toggle calls PUT .../posts/:postId/visibility", async () => {
    server.use(
      http.get(`http://localhost/api/v1/artists/${ARTIST_ID}/instagram/status`, () =>
        HttpResponse.json(CONNECTED_STATUS),
      ),
    );
    const user = userEvent.setup();
    renderTab();

    await screen.findByText("@ink_artist");
    await waitFor(() => expect(screen.getAllByRole("img")).toHaveLength(3));

    const [hideButton] = screen.getAllByRole("button", { name: /hide from portfolio/i });
    await user.click(hideButton);

    await waitFor(() => {
      expect(toggledPostId).toBe("post-1");
      expect(toggledVisibility).toBe(false);
    });
  });

  it("clicking Disconnect (after confirming) calls DELETE .../instagram/disconnect", async () => {
    server.use(
      http.get(`http://localhost/api/v1/artists/${ARTIST_ID}/instagram/status`, () =>
        HttpResponse.json(CONNECTED_STATUS),
      ),
    );
    vi.spyOn(window, "confirm").mockReturnValue(true);
    const user = userEvent.setup();
    renderTab();

    await screen.findByText("@ink_artist");
    await user.click(screen.getByRole("button", { name: /disconnect/i }));

    await waitFor(() => expect(disconnectCalled).toBe(true));
  });
});
