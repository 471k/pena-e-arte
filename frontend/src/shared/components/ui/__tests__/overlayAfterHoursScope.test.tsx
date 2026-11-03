import { describe, it, expect, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
// `?raw` gives index.css's source text (vite.config.ts lets this one stylesheet through
// Vitest's CSS stubbing), so the selectors under test are the ones that actually ship.
import indexCss from "../../../../index.css?raw";

import { Dialog, DialogContent, DialogDescription, DialogTitle, DialogTrigger } from "../dialog";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "../select";

// Radix portals every overlay to <body>, outside the role layout that carries
// data-theme="after-hours", so overlays cannot inherit that attribute. index.css instead
// gives the palette to body's direct children other than #root while a themed layout is
// mounted. jsdom can't resolve the cascade or prefers-color-scheme (e2e/accessibility.spec.ts
// does that in a real browser, both schemes), but it does evaluate `:has()` in
// Element.matches(), so this pins the selector logic against real Radix portal output.

const scopeSelectors: string[] = (() => {
  const src = indexCss.replace(/\/\*[\s\S]*?\*\//g, "");
  const m = /\n(\[data-theme="after-hours"\][^{]*)\{/.exec(src);
  if (!m) throw new Error("index.css: top-level After Hours scope block not found");
  return m[1].split(",").map((s) => s.replace(/\s+/g, " ").trim()).filter(Boolean);
})();

const inScope = (el: Element) => scopeSelectors.some((s) => el.matches(s));

/** The element Radix actually inserted under <body> for this overlay. */
function portalRoot(el: HTMLElement): HTMLElement {
  let node: HTMLElement = el;
  while (node.parentElement && node.parentElement !== document.body) node = node.parentElement;
  expect(node.parentElement).toBe(document.body);
  return node;
}

/** Render like the real app: inside <div id="root">, optionally under a themed layout root. */
function renderApp(ui: ReactNode, themed: boolean) {
  const root = document.createElement("div");
  root.id = "root";
  document.body.appendChild(root);
  return render(themed ? <div data-theme="after-hours">{ui}</div> : ui, { container: root });
}

function DialogDemo() {
  return (
    <Dialog>
      <DialogTrigger>Open dialog</DialogTrigger>
      <DialogContent>
        <DialogTitle>Cancel appointment?</DialogTitle>
        <DialogDescription>Review what happens to your deposit.</DialogDescription>
      </DialogContent>
    </Dialog>
  );
}

function SelectDemo() {
  return (
    <Select>
      <SelectTrigger aria-label="Duration"><SelectValue placeholder="Pick one" /></SelectTrigger>
      <SelectContent>
        <SelectItem value="1">1 hour</SelectItem>
        <SelectItem value="2">2 hours</SelectItem>
      </SelectContent>
    </Select>
  );
}

afterEach(() => {
  cleanup();
  document.body.innerHTML = "";
});

describe("overlays opened from an After Hours screen", () => {
  it("reads the three scope selectors from index.css", () => {
    expect(scopeSelectors).toEqual([
      '[data-theme="after-hours"]',
      'body:has([data-theme="after-hours"]) > :not(#root)',
      'body:has([data-theme="after-hours"]) [data-sonner-toaster]',
    ]);
  });

  it("a Dialog portalled to <body> from a themed screen is inside the scope", async () => {
    const user = userEvent.setup();
    renderApp(<DialogDemo />, true);
    await user.click(screen.getByRole("button", { name: "Open dialog" }));
    const dialog = await screen.findByRole("dialog", { name: "Cancel appointment?" });

    // Proves the premise: it really is outside the themed layout subtree…
    expect(dialog.closest("[data-theme]")).toBeNull();
    // …and still gets the palette through the portal rule.
    expect(inScope(portalRoot(dialog))).toBe(true);
  });

  it("a Select listbox portalled to <body> from a themed screen is inside the scope", async () => {
    const user = userEvent.setup();
    renderApp(<SelectDemo />, true);
    await user.click(screen.getByRole("combobox", { name: "Duration" }));
    const listbox = await screen.findByRole("listbox");
    expect(listbox.closest("[data-theme]")).toBeNull();
    expect(inScope(portalRoot(listbox))).toBe(true);
  });

  it("the same overlays on a public (unthemed) page stay outside the scope", async () => {
    const user = userEvent.setup();
    renderApp(<><DialogDemo /><SelectDemo /></>, false);
    await user.click(screen.getByRole("combobox", { name: "Duration" }));
    expect(inScope(portalRoot(await screen.findByRole("listbox")))).toBe(false);
    await user.keyboard("{Escape}");
    await user.click(screen.getByRole("button", { name: "Open dialog" }));
    expect(inScope(portalRoot(await screen.findByRole("dialog")))).toBe(false);
  });

  it("never themes #root itself, so public chrome beside the layout keeps its palette", () => {
    renderApp(<p>Studio page</p>, true);
    const root = document.getElementById("root");
    expect(root).not.toBeNull();
    expect(inScope(root!)).toBe(false);
    expect(inScope(document.body)).toBe(false);
    expect(inScope(document.documentElement)).toBe(false);
  });
});
