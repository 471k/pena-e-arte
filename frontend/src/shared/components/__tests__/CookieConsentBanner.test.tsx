import { describe, it, expect, afterEach, beforeEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CookieConsentBanner } from "@/shared/components/CookieConsentBanner";

describe("CookieConsentBanner", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    cleanup();
    localStorage.clear();
  });

  it("renders when consent has not been given", () => {
    render(<CookieConsentBanner />);
    expect(screen.getByRole("region", { name: /cookie consent/i })).toBeInTheDocument();
  });

  it("does not render when consent was already given", () => {
    localStorage.setItem("cookie-consent", "accepted");
    render(<CookieConsentBanner />);
    expect(screen.queryByRole("region", { name: /cookie consent/i })).not.toBeInTheDocument();
  });

  it("hides itself and persists consent when 'Got it' is clicked", async () => {
    const user = userEvent.setup();
    render(<CookieConsentBanner />);

    await user.click(screen.getByRole("button", { name: /got it/i }));

    expect(screen.queryByRole("region", { name: /cookie consent/i })).not.toBeInTheDocument();
    expect(localStorage.getItem("cookie-consent")).toBe("accepted");
  });
});
