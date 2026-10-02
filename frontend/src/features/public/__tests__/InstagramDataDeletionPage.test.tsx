import { describe, it, expect, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { InstagramDataDeletionPage } from "@/features/public";

function renderAt(url: string) {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <InstagramDataDeletionPage />
    </MemoryRouter>,
  );
}

describe("InstagramDataDeletionPage", () => {
  afterEach(cleanup);

  it("confirms the deletion and echoes the confirmation code Meta passed in the URL", () => {
    renderAt("/data-deletion/instagram?code=abc123def456");

    expect(screen.getByRole("heading", { name: /instagram data deletion/i })).toBeInTheDocument();
    expect(screen.getByText(/request was received and completed/i)).toBeInTheDocument();
    expect(screen.getByText("abc123def456")).toBeInTheDocument();
  });

  it("without a code, explains how deletion works and claims no completed request", () => {
    renderAt("/data-deletion/instagram");

    expect(screen.getByText(/apps and websites/i)).toBeInTheDocument();
    expect(screen.queryByText(/confirmation code/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/request was received and completed/i)).not.toBeInTheDocument();
  });

  it("links to the privacy policy and contact page", () => {
    renderAt("/data-deletion/instagram?code=x");

    expect(screen.getByRole("link", { name: /privacy policy/i })).toHaveAttribute("href", "/privacy");
    expect(screen.getByRole("link", { name: /contact us/i })).toHaveAttribute("href", "/contact");
  });
});
