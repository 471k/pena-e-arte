import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { ArtistModeSwitcher } from "@/shared/components/ArtistModeSwitcher";

function renderSwitcher(initialPath: string) {
  render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="*" element={<ArtistModeSwitcher artistId="art-1" />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("ArtistModeSwitcher", () => {
  it("renders both Owner and Artist tabs", () => {
    renderSwitcher("/dashboard");
    expect(screen.getByRole("tab", { name: /owner/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /artist/i })).toBeInTheDocument();
  });

  it("marks Owner as selected on a non-artist route", () => {
    renderSwitcher("/dashboard");
    expect(screen.getByRole("tab", { name: /owner/i })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: /artist/i })).toHaveAttribute("aria-selected", "false");
  });

  it("marks Artist as selected when on the linked artist's own profile route", () => {
    renderSwitcher("/artists/art-1");
    expect(screen.getByRole("tab", { name: /artist/i })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: /owner/i })).toHaveAttribute("aria-selected", "false");
  });

  it("marks Artist as selected on the earnings route", () => {
    renderSwitcher("/earnings");
    expect(screen.getByRole("tab", { name: /artist/i })).toHaveAttribute("aria-selected", "true");
  });

  it("marks Owner as selected when viewing a DIFFERENT artist's profile (not the owner's own)", () => {
    renderSwitcher("/artists/some-other-artist");
    expect(screen.getByRole("tab", { name: /owner/i })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: /artist/i })).toHaveAttribute("aria-selected", "false");
  });

  it("clicking Artist navigates to the owner's own artist profile", async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter initialEntries={["/dashboard"]}>
        <Routes>
          <Route path="/dashboard" element={<ArtistModeSwitcher artistId="art-1" />} />
          <Route path="/artists/art-1" element={<div data-testid="artist-page" />} />
        </Routes>
      </MemoryRouter>,
    );

    await user.click(screen.getByRole("tab", { name: /artist/i }));

    expect(await screen.findByTestId("artist-page")).toBeInTheDocument();
  });

  it("clicking Owner navigates to the dashboard", async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter initialEntries={["/artists/art-1"]}>
        <Routes>
          <Route path="/artists/art-1" element={<ArtistModeSwitcher artistId="art-1" />} />
          <Route path="/dashboard" element={<div data-testid="dashboard-page" />} />
        </Routes>
      </MemoryRouter>,
    );

    await user.click(screen.getByRole("tab", { name: /owner/i }));

    expect(await screen.findByTestId("dashboard-page")).toBeInTheDocument();
  });
});
