import { describe, it, expect, afterEach } from "vitest";
import { render, screen, cleanup, act } from "@testing-library/react";
import { toast } from "sonner";
import type { ReactNode } from "react";
import indexCss from "../../../../index.css?raw";
import { Toaster } from "../sonner";

// The toaster resolves its colours from index.css's Sonner-variable rule, which reads app
// tokens, and the toaster receives the After Hours tokens through the scope selectors.
// jsdom cannot resolve the cascade (the painted colours are asserted in a real browser in
// e2e/accessibility.spec.ts, both schemes, in and out of scope), but it does evaluate these
// selectors in Element.matches() — so this pins that a REAL rendered toaster is matched by
// the toast rule everywhere, and by the After Hours scope only under a themed layout.

const src = indexCss.replace(/\/\*[\s\S]*?\*\//g, "");
const TOAST_RULE = ":root [data-sonner-toaster][data-sonner-theme]";
const scopeSelectors: string[] = (() => {
  const m = /\n(\[data-theme="after-hours"\][^{]*)\{/.exec(src);
  if (!m) throw new Error("index.css: top-level After Hours scope block not found");
  return m[1].split(",").map((s) => s.replace(/\s+/g, " ").trim()).filter(Boolean);
})();

function renderApp(ui: ReactNode, themed: boolean) {
  const root = document.createElement("div");
  root.id = "root";
  document.body.appendChild(root);
  // Like main.tsx: the Toaster sits beside the router, never inside a role layout.
  return render(
    <>
      {themed ? <div data-theme="after-hours">screen</div> : <div>public page</div>}
      {ui}
    </>,
    { container: root },
  );
}

async function showToast(message: string): Promise<{ toaster: HTMLElement; item: HTMLElement }> {
  act(() => { toast.success(message); });
  const text = await screen.findByText(message);
  const item = text.closest("[data-sonner-toast]") as HTMLElement;
  const toaster = text.closest("[data-sonner-toaster]") as HTMLElement;
  expect(item).not.toBeNull();
  expect(toaster).not.toBeNull();
  return { toaster, item };
}

afterEach(() => {
  act(() => { toast.dismiss(); });
  cleanup();
  document.body.innerHTML = "";
});

describe("Toaster theming", () => {
  it("index.css carries the toast variable rule", () => {
    expect(src).toContain(`${TOAST_RULE} {`);
  });

  it("outside the After Hours scope: matched by the toast rule, not by the scope", async () => {
    renderApp(<Toaster />, false);
    const { toaster, item } = await showToast("Saved");
    expect(toaster.matches(TOAST_RULE)).toBe(true);
    expect(toaster.getAttribute("data-sonner-theme")).toMatch(/^(light|dark)$/);
    expect(scopeSelectors.some((s) => toaster.matches(s))).toBe(false);
    // Sonner paints it (data-styled) — no dead Tailwind colour classes passed through.
    expect(item.getAttribute("data-styled")).toBe("true");
    expect(item.className).not.toMatch(/\b(bg|text|border)-/);
  });

  it("under a themed layout: matched by the toast rule AND the After Hours scope", async () => {
    renderApp(<Toaster />, true);
    const { toaster } = await showToast("Booked");
    expect(toaster.closest("[data-theme]")).toBeNull(); // the premise: not inside the layout
    expect(toaster.matches(TOAST_RULE)).toBe(true);
    expect(toaster.matches('body:has([data-theme="after-hours"]) [data-sonner-toaster]')).toBe(true);
  });
});
