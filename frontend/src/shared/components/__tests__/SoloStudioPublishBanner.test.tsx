import { describe, it, expect, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";

import { SoloStudioPublishBanner } from "@/shared/components/SoloStudioPublishBanner";
import type { StudioResponse } from "@/features/studios/studiosApi";

const BASE_STUDIO: StudioResponse = {
  id:                   "s1",
  name:                 "Jane Doe",
  slug:                 "jane-doe",
  city:                 "",
  latitude:             0,
  longitude:            0,
  showPlatformBranding: true,
  allowBrandingRemoval: false,
  trialExpiresAt:       "2099-01-01T00:00:00Z",
  createdAt:            "2024-01-01T00:00:00Z",
  isActive:             true,
  slugLockedAt:         null,
  phoneNumber:          null,
  instagramHandle:      null,
  nipt:                 null,
  isSolo:               true,
  isPublished:          false,
};

function renderBanner(studio?: StudioResponse) {
  return render(
    <MemoryRouter>
      <SoloStudioPublishBanner studio={studio} />
    </MemoryRouter>,
  );
}

afterEach(() => {
  sessionStorage.clear();
  cleanup();
});

describe("SoloStudioPublishBanner", () => {
  it("renders for an unpublished solo studio", () => {
    renderBanner(BASE_STUDIO);
    expect(screen.getByText(/finish setting up your studio/i)).toBeInTheDocument();
  });

  it("does not render for a non-solo studio", () => {
    renderBanner({ ...BASE_STUDIO, isSolo: false });
    expect(screen.queryByText(/finish setting up your studio/i)).not.toBeInTheDocument();
  });

  it("does not render for a published solo studio", () => {
    renderBanner({ ...BASE_STUDIO, isPublished: true });
    expect(screen.queryByText(/finish setting up your studio/i)).not.toBeInTheDocument();
  });

  it("does not render when studio is undefined", () => {
    const { container } = renderBanner(undefined);
    expect(container.firstChild).toBeNull();
  });

  it("links to Studio Settings", () => {
    renderBanner(BASE_STUDIO);
    const link = screen.getByRole("link", { name: /studio settings/i });
    expect(link.getAttribute("href")).toBe("/studios/me");
  });

  it("dismisses on click and stays dismissed for the session", async () => {
    const user = userEvent.setup();
    renderBanner(BASE_STUDIO);

    await user.click(screen.getByRole("button", { name: /dismiss/i }));

    expect(screen.queryByText(/finish setting up your studio/i)).not.toBeInTheDocument();
    expect(sessionStorage.getItem("solo-studio-publish-banner-dismissed")).toBe("1");
  });

  it("stays dismissed across a remount within the same session", async () => {
    const user = userEvent.setup();
    const { unmount } = renderBanner(BASE_STUDIO);
    await user.click(screen.getByRole("button", { name: /dismiss/i }));
    unmount();

    renderBanner(BASE_STUDIO);
    expect(screen.queryByText(/finish setting up your studio/i)).not.toBeInTheDocument();
  });
});
