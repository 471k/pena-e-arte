import { describe, it, expect, beforeAll, afterEach, afterAll, vi } from "vitest";
import { render, screen, cleanup, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { artistsApi } from "@/features/artists/artistsApi";
import type { InstagramConnectionStatus } from "@/features/artists/artistsApi";
import { socialApi } from "@/features/social/socialApi";
import type { SocialLinkStatus } from "@/features/social/socialApi";
import { ArtistSocialTab } from "@/features/artists/components/ArtistSocialTab";

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn(), info: vi.fn() } }));

const ARTIST_ID = "artist-0001";
const BASE = `http://localhost/api/v1/artists/${ARTIST_ID}`;

const DISCONNECTED: InstagramConnectionStatus = { isConnected: false, username: null, lastSyncedAt: null, postCount: 0 };

function makeLinks(overrides: Partial<Record<string, Partial<SocialLinkStatus>>> = {}): SocialLinkStatus[] {
  const platforms = ["Instagram", "TikTok", "Facebook", "X", "YouTube"] as const;
  return platforms.map((platform) => ({
    platform,
    handle: null,
    isVerified: false,
    verifiedAt: null,
    verificationMethod: null,
    isOAuthConfigured: platform === "Instagram" || platform === "TikTok",
    isManualCheckSupported: platform !== "TikTok",
    hasPendingCode: false,
    pendingCodeExpiresAt: null,
    ...overrides[platform],
  }));
}

const server = setupServer(
  http.get(`${BASE}/instagram/status`, () => HttpResponse.json(DISCONNECTED)),
  http.get(`${BASE}/instagram/posts`, () => HttpResponse.json([])),
  http.get(`${BASE}/social`, () => HttpResponse.json(makeLinks())),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); vi.clearAllMocks(); cleanup(); });
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      ui: uiReducer,
      [artistsApi.reducerPath]: artistsApi.reducer,
      [socialApi.reducerPath]: socialApi.reducer,
    },
    middleware: (gd) => gd().concat(artistsApi.middleware, socialApi.middleware),
    preloadedState: {
      auth: {
        user: { id: "u1", email: "owner@ink.test" },
        token: "fake-token",
        tenantId: "stud-0001",
        role: "owner",
        pendingReferralCode: null, impersonation: null,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any,
      ui: { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null, impersonationScopeError: null, impersonationSessionExpired: false },
    },
  });
}

interface TabProps { canManage: boolean; isOwnProfile: boolean; slug?: string | null }

function renderTab({ canManage, isOwnProfile, slug = "rui-tavares" }: TabProps) {
  render(
    <Provider store={makeStore()}>
      <ArtistSocialTab
        artistId={ARTIST_ID}
        firstName="Rui"
        slug={slug}
        canManage={canManage}
        isOwnProfile={isOwnProfile}
      />
    </Provider>,
  );
}

async function untilLoaded() {
  await screen.findByRole("heading", { name: "Connected accounts" });
}

describe("ArtistSocialTab", () => {
  it("shows one skeleton block of five row-shaped placeholders while loading, not two separate sets", () => {
    renderTab({ canManage: true, isOwnProfile: false });

    const loading = screen.getByLabelText("Loading connected accounts");
    expect(loading).toHaveAttribute("aria-busy", "true");
    expect(loading.children).toHaveLength(5);
  });

  describe("owner viewing an artist", () => {
    it("lists all five platforms as one labelled list, third-person helper, each with a status badge", async () => {
      renderTab({ canManage: true, isOwnProfile: false });
      await untilLoaded();

      const list = screen.getByRole("list", { name: "Connected accounts" });
      const items = within(list).getAllByRole("listitem");
      expect(items).toHaveLength(5);
      for (const name of ["Instagram", "TikTok", "Facebook", "X", "YouTube"]) {
        expect(within(list).getByText(name)).toBeInTheDocument();
      }
      expect(within(list).getAllByText("Not linked")).toHaveLength(5);
      expect(
        screen.getByText("Link Rui's accounts so clients can find them and see a Verified badge on their public profile."),
      ).toBeInTheDocument();
    });

    it("gives every row exactly one primary action (Connect / Get code) and never a bare dash", async () => {
      renderTab({ canManage: true, isOwnProfile: false });
      await untilLoaded();

      expect(screen.getByRole("button", { name: "Connect Instagram" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Connect TikTok" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Get Facebook verification code" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Get X verification code" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Get YouTube verification code" })).toBeInTheDocument();
      expect(screen.getByRole("list", { name: "Connected accounts" })).not.toHaveTextContent("—");
    });

    it("has a single h2 heading for the section and no public-profile link (that is the artist's own)", async () => {
      renderTab({ canManage: true, isOwnProfile: false });
      await untilLoaded();

      expect(screen.getAllByRole("heading").map((h) => h.tagName)).toEqual(["H2"]);
      expect(screen.queryByRole("link", { name: /view public profile/i })).not.toBeInTheDocument();
    });
  });

  describe("artist on their own profile (self-service)", () => {
    it("can act on every row, with second-person copy and no 'ask your owner' dead end", async () => {
      renderTab({ canManage: false, isOwnProfile: true });
      await untilLoaded();

      expect(
        screen.getByText("Link your accounts so clients can find you and see a Verified badge on your public profile."),
      ).toBeInTheDocument();
      expect(screen.queryByText(/studio owner manages/i)).not.toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Connect Instagram" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Connect TikTok" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Get Facebook verification code" })).toBeInTheDocument();
      expect(screen.getByRole("textbox", { name: "Facebook handle" })).toBeInTheDocument();
      expect(screen.getByText(
        "Connect Instagram to automatically show your latest posts on your public portfolio.",
      )).toBeInTheDocument();
    });

    it("starts the connect flow for their OWN artist id, not any other", async () => {
      let requestedUrl: string | null = null;
      server.use(
        http.get(`${BASE}/instagram/connect-url`, ({ request }) => {
          requestedUrl = request.url;
          return HttpResponse.json({ authUrl: "https://api.instagram.com/oauth/authorize?x=1" });
        }),
      );
      const popup = { location: { href: "" }, close: vi.fn() };
      const openSpy = vi.spyOn(window, "open").mockReturnValue(popup as unknown as Window);
      const user = userEvent.setup();
      renderTab({ canManage: false, isOwnProfile: true });
      await untilLoaded();

      await user.click(screen.getByRole("button", { name: "Connect Instagram" }));

      await waitFor(() => expect(popup.location.href).toBe("https://api.instagram.com/oauth/authorize?x=1"));
      expect(requestedUrl).toBe(`${BASE}/instagram/connect-url`);
      openSpy.mockRestore();
    });

    it("saves a typed handle against their own artist id and platform", async () => {
      let saved: { url: string; body: unknown } | null = null;
      server.use(
        http.put(`${BASE}/social/:platform/handle`, async ({ request }) => {
          saved = { url: request.url, body: await request.json() };
          return new HttpResponse(null, { status: 204 });
        }),
      );
      const user = userEvent.setup();
      renderTab({ canManage: false, isOwnProfile: true });
      await untilLoaded();

      await user.type(screen.getByRole("textbox", { name: "Facebook handle" }), "rui.ink");
      await user.tab();

      await waitFor(() => expect(saved).not.toBeNull());
      expect(saved!.url).toBe(`${BASE}/social/Facebook/handle`);
      expect(saved!.body).toEqual({ handle: "rui.ink" });
    });

    it("links to the public profile when the artist has a slug", async () => {
      renderTab({ canManage: false, isOwnProfile: true, slug: "rui-tavares" });
      await untilLoaded();

      const link = screen.getByRole("link", { name: /view public profile/i });
      expect(link).toHaveAttribute("href", expect.stringMatching(/\/artist\/rui-tavares$/));
      expect(link).toHaveAttribute("target", "_blank");
      expect(link).toHaveAttribute("rel", expect.stringContaining("noopener"));
    });

    it("omits the public-profile link when there is no slug", async () => {
      renderTab({ canManage: false, isOwnProfile: true, slug: null });
      await untilLoaded();

      expect(screen.queryByRole("link", { name: /view public profile/i })).not.toBeInTheDocument();
    });
  });

  describe("artist viewing another artist (defensive branch)", () => {
    it("is fully read-only with the neutral explanation", async () => {
      renderTab({ canManage: false, isOwnProfile: false });
      await untilLoaded();

      expect(screen.getByText("Only the studio owner manages connections for this profile.")).toBeInTheDocument();
      expect(screen.queryByRole("button")).not.toBeInTheDocument();
      expect(screen.queryByRole("link", { name: /view public profile/i })).not.toBeInTheDocument();
    });
  });

  describe("error state", () => {
    it("shows an alert with Try again and never falls through to a 'Not linked' empty state", async () => {
      server.use(http.get(`${BASE}/instagram/status`, () => new HttpResponse(null, { status: 500 })));
      renderTab({ canManage: true, isOwnProfile: false });

      expect(await screen.findByText("We couldn't load Rui's connected accounts.")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Try again" })).toBeInTheDocument();
      expect(screen.queryByText("Not linked")).not.toBeInTheDocument();
    });

    it("uses second person on the artist's own profile", async () => {
      server.use(http.get(`${BASE}/social`, () => new HttpResponse(null, { status: 500 })));
      renderTab({ canManage: false, isOwnProfile: true });

      expect(await screen.findByText("We couldn't load your connected accounts.")).toBeInTheDocument();
    });

    it("Try again refetches both queries and recovers", async () => {
      let failStatus = true;
      server.use(
        http.get(`${BASE}/instagram/status`, () =>
          failStatus ? new HttpResponse(null, { status: 500 }) : HttpResponse.json(DISCONNECTED),
        ),
      );
      const user = userEvent.setup();
      renderTab({ canManage: true, isOwnProfile: false });

      const retry = await screen.findByRole("button", { name: "Try again" });
      failStatus = false;
      await user.click(retry);

      expect(await screen.findByRole("heading", { name: "Connected accounts" })).toBeInTheDocument();
      expect(screen.queryByText(/couldn't load/i)).not.toBeInTheDocument();
    });
  });

  it("puts the connected Instagram row's synced-posts panel inside the Instagram list item", async () => {
    server.use(
      http.get(`${BASE}/instagram/status`, () =>
        HttpResponse.json({ isConnected: true, username: "ink_artist", lastSyncedAt: "2026-07-01T10:00:00Z", postCount: 1 }),
      ),
      http.get(`${BASE}/instagram/posts`, () =>
        HttpResponse.json([
          { id: "post-1", instagramMediaId: "m1", mediaUrl: "https://img/1.jpg", thumbnailUrl: null, caption: "a", mediaType: "IMAGE", postedAt: "2026-07-01T09:00:00Z", isVisible: true },
        ]),
      ),
    );
    renderTab({ canManage: true, isOwnProfile: false });
    await untilLoaded();

    const [instagramItem] = screen.getAllByRole("listitem");
    expect(await within(instagramItem).findByText("@ink_artist")).toBeInTheDocument();
    expect(await within(instagramItem).findByRole("img")).toBeInTheDocument();
    expect(within(instagramItem).getByRole("button", { name: "Disconnect Instagram" })).toBeInTheDocument();
  });
});
