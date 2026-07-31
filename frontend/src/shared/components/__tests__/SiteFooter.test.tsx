import { describe, it, expect, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { SiteFooter } from "@/shared/components/SiteFooter";
import {
  LEGAL_ENTITY_NAME,
  LEGAL_ENTITY_NIPT,
} from "@/shared/constants/legalEntity";

function renderFooter() {
  render(
    <MemoryRouter>
      <SiteFooter />
    </MemoryRouter>,
  );
}

describe("SiteFooter", () => {
  afterEach(cleanup);

  it("discloses the operating legal entity and NIPT from the single source of truth", () => {
    renderFooter();
    expect(
      screen.getByText(
        new RegExp(`operated by ${LEGAL_ENTITY_NAME}, NIPT ${LEGAL_ENTITY_NIPT}`, "i"),
      ),
    ).toBeInTheDocument();
  });

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
