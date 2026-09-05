import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { VerifiedSocialBadge } from "../VerifiedSocialBadge";

describe("VerifiedSocialBadge", () => {
  it("renders the word 'Verified'", () => {
    render(<VerifiedSocialBadge platform="Instagram" />);
    expect(screen.getByText(/verified/i)).toBeInTheDocument();
  });

  it("includes the platform name in its tooltip title", () => {
    render(<VerifiedSocialBadge platform="TikTok" />);
    expect(screen.getByText(/verified/i).getAttribute("title")).toContain("TikTok");
  });
});
