import { describe, it, expect, beforeAll, afterEach, afterAll, vi } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { toast } from "sonner";

import authReducer from "@/features/auth/authSlice";
import { socialApi } from "@/features/social/socialApi";
import type { SocialLinkStatus, SocialConnectUrlResponse, SocialVerificationCodeResponse, SocialVerifyResult } from "@/features/social/socialApi";
import { SocialLinksCard } from "@/features/social/components/SocialLinksCard";

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn(), info: vi.fn() } }));

const SUBJECT_ID = "studio-0001";
const BASE = `http://localhost/api/v1/studios/${SUBJECT_ID}/social`;

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

let requestedCodePlatform: string | null = null;
let verifiedPlatform: string | null = null;
let disconnectedPlatform: string | null = null;

const server = setupServer(
  http.get(BASE, () => HttpResponse.json(makeLinks())),
  http.get(`${BASE}/:platform/connect-url`, () =>
    HttpResponse.json({ authUrl: "https://tiktok.com/authorize?x=1" } satisfies SocialConnectUrlResponse),
  ),
  http.post(`${BASE}/:platform/request-code`, ({ params }) => {
    requestedCodePlatform = params.platform as string;
    return HttpResponse.json({ code: "PENA-ABC123", expiresAt: "2026-08-24T00:00:00Z" } satisfies SocialVerificationCodeResponse);
  }),
  http.post(`${BASE}/:platform/verify-code`, ({ params }) => {
    verifiedPlatform = params.platform as string;
    return HttpResponse.json({ verified: true, failureReason: null } satisfies SocialVerifyResult);
  }),
  http.delete(`${BASE}/:platform/disconnect`, ({ params }) => {
    disconnectedPlatform = params.platform as string;
    return new HttpResponse(null, { status: 204 });
  }),
  http.put(`${BASE}/:platform/handle`, () => new HttpResponse(null, { status: 204 })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  server.resetHandlers();
  requestedCodePlatform = null;
  verifiedPlatform = null;
  disconnectedPlatform = null;
  vi.clearAllMocks();
  cleanup();
});
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: { auth: authReducer, [socialApi.reducerPath]: socialApi.reducer },
    middleware: (gd) => gd().concat(socialApi.middleware),
    preloadedState: {
      auth: {
        user: { id: "u1", email: "owner@ink.test" },
        token: "fake-token",
        tenantId: SUBJECT_ID,
        role: "owner",
        pendingReferralCode: null,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any,
    },
  });
}

function renderCard() {
  render(
    <Provider store={makeStore()}>
      <SocialLinksCard subjectType="Studio" subjectId={SUBJECT_ID} />
    </Provider>,
  );
}

describe("SocialLinksCard", () => {
  it("renders one row per platform", async () => {
    renderCard();
    expect(await screen.findByText("Instagram")).toBeInTheDocument();
    expect(screen.getByText("TikTok")).toBeInTheDocument();
    expect(screen.getByText("Facebook")).toBeInTheDocument();
    expect(screen.getByText("X")).toBeInTheDocument();
    expect(screen.getByText("YouTube")).toBeInTheDocument();
  });

  it("shows a Connect button for an OAuth-configured, unverified platform", async () => {
    renderCard();
    await screen.findByText("Instagram");
    const connectButtons = screen.getAllByRole("button", { name: /^connect$/i });
    expect(connectButtons.length).toBeGreaterThan(0);
  });

  it("shows 'Not available yet' for a platform with neither OAuth nor manual check configured", async () => {
    server.use(
      http.get(BASE, () =>
        HttpResponse.json(makeLinks({ TikTok: { isOAuthConfigured: false, isManualCheckSupported: false } })),
      ),
    );
    renderCard();
    await screen.findByText("TikTok");
    expect(screen.getByText(/not available yet/i)).toBeInTheDocument();
  });

  it("shows the Verified badge and Disconnect button for a verified platform", async () => {
    server.use(
      http.get(BASE, () =>
        HttpResponse.json(makeLinks({ Instagram: { isVerified: true, handle: "studiohandle" } })),
      ),
    );
    renderCard();

    expect(await screen.findByText("@studiohandle")).toBeInTheDocument();
    expect(screen.getByText(/verified/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /disconnect/i })).toBeInTheDocument();
  });

  it("clicking Connect opens a blank tab synchronously, then navigates it to the authUrl", async () => {
    server.use(
      http.get(BASE, () =>
        HttpResponse.json(makeLinks({
          Instagram: { isOAuthConfigured: false },
          TikTok: { isOAuthConfigured: true },
        })),
      ),
    );
    const popup = { location: { href: "" }, close: vi.fn() };
    const openSpy = vi.spyOn(window, "open").mockReturnValue(popup as unknown as Window);
    const user = userEvent.setup();
    renderCard();

    await screen.findByText("TikTok");
    const [connectButton] = screen.getAllByRole("button", { name: /^connect$/i });
    await user.click(connectButton);

    expect(openSpy).toHaveBeenCalledWith("about:blank", "_blank");
    await waitFor(() => expect(popup.location.href).toBe("https://tiktok.com/authorize?x=1"));

    openSpy.mockRestore();
  });

  it("requesting a code, then verifying, calls request-code then verify-code for the same platform", async () => {
    server.use(
      http.get(BASE, () =>
        HttpResponse.json(makeLinks({
          YouTube: { isOAuthConfigured: false, isManualCheckSupported: true, handle: "studiochannel" },
        })),
      ),
    );
    const user = userEvent.setup();
    renderCard();

    await screen.findByText("YouTube");
    const getCodeButtons = screen.getAllByRole("button", { name: /get verification code/i });
    await user.click(getCodeButtons[getCodeButtons.length - 1]);

    await waitFor(() => expect(requestedCodePlatform).toBe("YouTube"));
    expect(await screen.findByText("PENA-ABC123")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /i've added it/i }));

    await waitFor(() => expect(verifiedPlatform).toBe("YouTube"));
    expect(toast.success).toHaveBeenCalled();
  });

  it("clicking Disconnect (after confirming) calls DELETE .../disconnect for that platform", async () => {
    server.use(
      http.get(BASE, () =>
        HttpResponse.json(makeLinks({ Instagram: { isVerified: true, handle: "studiohandle" } })),
      ),
    );
    vi.spyOn(window, "confirm").mockReturnValue(true);
    const user = userEvent.setup();
    renderCard();

    await screen.findByText("@studiohandle");
    await user.click(screen.getByRole("button", { name: /disconnect/i }));

    await waitFor(() => expect(disconnectedPlatform).toBe("Instagram"));
  });
});
