import { describe, it, expect, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { SiteFooter } from "@/shared/components/SiteFooter";

function renderFooter() {
  render(
    <MemoryRouter>
      <SiteFooter />
    </MemoryRouter>,
  );
}

describe("SiteFooter", () => {
  afterEach(cleanup);

  it("renders the current year in the copyright line", () => {
    renderFooter();
    const year = String(new Date().getFullYear());
    expect(screen.getByText(new RegExp(`© ${year} TattooOS`))).toBeInTheDocument();
  });

  it.each([
    ["Privacy Policy", "/privacy"],
    ["Terms of Service", "/terms"],
    ["Refund Policy", "/refund-policy"],
    ["Contact", "/contact"],
  ])("links %s to the real route %s", (label, href) => {
    renderFooter();
    expect(screen.getByRole("link", { name: label })).toHaveAttribute("href", href);
  });
});
