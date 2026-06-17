import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

import { SuspensionBanner } from "@/shared/components/SuspensionBanner";
import type { StudioResponse } from "@/features/studios/studiosApi";

const SUSPENDED_STUDIO: StudioResponse = {
  id:                   "s1",
  name:                 "Ink Soul",
  slug:                 "ink-soul",
  city:                 "Porto",
  latitude:             41.15,
  longitude:            -8.61,
  showPlatformBranding: true,
  allowBrandingRemoval: false,
  trialExpiresAt:       "2099-01-01T00:00:00Z",
  createdAt:            "2024-01-01T00:00:00Z",
  isActive:             false,
};

const ACTIVE_STUDIO: StudioResponse = { ...SUSPENDED_STUDIO, isActive: true };

function renderBanner(studio?: StudioResponse) {
  render(
    <MemoryRouter>
      <SuspensionBanner studio={studio} />
    </MemoryRouter>,
  );
}

describe("SuspensionBanner", () => {
  it("renders without crashing when studio is suspended", () => {
    renderBanner(SUSPENDED_STUDIO);
    expect(screen.getByText(/suspended/i)).toBeInTheDocument();
  });

  it("does not render when studio is active", () => {
    renderBanner(ACTIVE_STUDIO);
    expect(screen.queryByText(/suspended/i)).not.toBeInTheDocument();
  });

  it("does not render when studio is undefined", () => {
    const { container } = render(
      <MemoryRouter>
        <SuspensionBanner />
      </MemoryRouter>,
    );
    expect(container.firstChild).toBeNull();
  });

  it("contains text about suspension", () => {
    renderBanner(SUSPENDED_STUDIO);
    expect(screen.getByText(/platform administrator/i)).toBeInTheDocument();
  });

  it("contains a link to /subscribe", () => {
    renderBanner(SUSPENDED_STUDIO);
    const link = screen.getByRole("link", { name: /reactivate your subscription/i });
    expect(link.getAttribute("href")).toContain("/subscribe");
  });
});
