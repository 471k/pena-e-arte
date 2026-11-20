import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { Link, RouterProvider, createMemoryRouter } from "react-router-dom";
import { UnsavedChangesGuard } from "@/shared/components/UnsavedChangesGuard";

function FormPage() {
  const [value, setValue] = useState("");
  return (
    <div>
      <label htmlFor="desc">Description</label>
      <input id="desc" value={value} onChange={(e) => setValue(e.target.value)} />
      <UnsavedChangesGuard when={value !== ""} />
      <Link to="/elsewhere">Go elsewhere</Link>
    </div>
  );
}

function renderInDataRouter() {
  const router = createMemoryRouter([
    { path: "/book", element: <FormPage /> },
    { path: "/elsewhere", element: <p>Elsewhere page</p> },
  ], { initialEntries: ["/book"] });
  render(<RouterProvider router={router} />);
  return router;
}

describe("UnsavedChangesGuard (S04)", () => {
  it("lets the person leave freely when nothing has been entered", async () => {
    const user = userEvent.setup();
    renderInDataRouter();
    await user.click(screen.getByRole("link", { name: /go elsewhere/i }));
    expect(await screen.findByText("Elsewhere page")).toBeInTheDocument();
  });

  it("with details entered, leaving asks first; Stay keeps everything, Leave goes", async () => {
    const user = userEvent.setup();
    const router = renderInDataRouter();
    await user.type(screen.getByLabelText("Description"), "A small rose");

    await user.click(screen.getByRole("link", { name: /go elsewhere/i }));
    expect(await screen.findByRole("alertdialog", { name: /leave this page/i })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /stay on this page/i }));
    expect(router.state.location.pathname).toBe("/book");
    expect(screen.getByLabelText("Description")).toHaveValue("A small rose");

    await user.click(screen.getByRole("link", { name: /go elsewhere/i }));
    await user.click(await screen.findByRole("button", { name: /leave and lose it/i }));
    expect(await screen.findByText("Elsewhere page")).toBeInTheDocument();
  });

  it("closing or reloading the tab gets the browser's prompt only while details are entered", async () => {
    const user = userEvent.setup();
    renderInDataRouter();
    const before = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(before);
    expect(before.defaultPrevented).toBe(false);

    await user.type(screen.getByLabelText("Description"), "x");
    const after = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(after);
    expect(after.defaultPrevented).toBe(true);
  });
});
