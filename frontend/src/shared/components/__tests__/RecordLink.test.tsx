import { describe, it, expect, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useNavigate } from "react-router-dom";

import { RecordLink } from "@/shared/components/RecordLink";
import { resetReturnTarget } from "@/shared/components/recordLinkContract";

afterEach(() => resetReturnTarget());

function List() {
  return (
    <ul>
      <li><RecordLink to="/things/1">First thing</RecordLink></li>
      <li><RecordLink to="/things/2">Second thing</RecordLink></li>
    </ul>
  );
}

function Detail() {
  const navigate = useNavigate();
  return (
    <>
      <p>Detail page</p>
      <button type="button" onClick={() => navigate(-1)}>Browser back</button>
      <button type="button" onClick={() => navigate("/things")}>Back to things</button>
    </>
  );
}

function renderApp() {
  render(
    <MemoryRouter initialEntries={["/things"]}>
      <Routes>
        <Route path="/things" element={<List />} />
        <Route path="/things/:id" element={<Detail />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("RecordLink", () => {
  it("is a named native link to the record", () => {
    renderApp();
    expect(screen.getByRole("link", { name: "Second thing" })).toHaveAttribute("href", "/things/2");
  });

  it("coming back by browser back focuses the record you opened", async () => {
    const user = userEvent.setup();
    renderApp();
    screen.getByRole("link", { name: "Second thing" }).focus();
    await user.keyboard("{Enter}");
    expect(await screen.findByText("Detail page")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Browser back" }));

    expect(await screen.findByRole("link", { name: "Second thing" })).toHaveFocus();
  });

  it("a forward visit to the list (a Back button, the sidebar) does not move focus", async () => {
    const user = userEvent.setup();
    renderApp();
    await user.click(screen.getByRole("link", { name: "Second thing" }));
    await user.click(await screen.findByRole("button", { name: "Back to things" }));

    expect(await screen.findByRole("link", { name: "Second thing" })).not.toHaveFocus();
  });

  it("writes nothing to browser storage", async () => {
    const user = userEvent.setup();
    sessionStorage.clear();
    localStorage.clear();
    renderApp();
    await user.click(screen.getByRole("link", { name: "First thing" }));
    expect(sessionStorage.length).toBe(0);
    expect(localStorage.length).toBe(0);
  });
});
