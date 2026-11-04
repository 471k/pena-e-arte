import { describe, it, expect } from "vitest";
// `?raw` returns the stylesheet source untransformed (vite.config.ts lets these through).
import indexCss from "../../index.css?raw";
import sonnerCss from "sonner/dist/styles.css?raw";
import { contrast, parseColour, parseCssBlocks, type Rgb } from "@/test/contrast";

// Toast theming (accessibility cleanup, 2026-11-04).
//
// Sonner 2.0.8 ships unlayered CSS, which beats Tailwind v4's layered utilities regardless of
// specificity, so toasts are themed through Sonner's own custom properties in index.css. This
// file pins that wiring and recomputes, from the stylesheets themselves, the WCAG contrast of
// every toast pair in all four contexts a toast can render in: light and dark, outside and
// inside the After Hours scope. The painted colours of a real toast are asserted in
// e2e/accessibility.spec.ts (jsdom cannot resolve the cascade).

const blocks = parseCssBlocks(indexCss);
const DARK = "@media (prefers-color-scheme: dark)";
const TOAST_SELECTOR = ":root [data-sonner-toaster][data-sonner-theme]";
const isScope = (h: string) => h.startsWith('[data-theme="after-hours"]');

const theme = blocks.find((b) => b.path.length === 1 && b.path[0] === "@theme");
const darkRoots = blocks.filter((b) => b.path.length === 2 && b.path[0] === DARK && b.path[1] === ":root");
const scopedLight = blocks.find((b) => b.path.length === 1 && isScope(b.path[0]));
const scopedDark = blocks.find((b) => b.path.length === 2 && b.path[0] === DARK && isScope(b.path[1]));
const toastRule = blocks.find((b) => b.path.length === 1 && b.path[0] === TOAST_SELECTOR);

type Context = "light" | "dark" | "after-hours light" | "after-hours dark";

/** The value a token resolves to in a context, following the cascade index.css sets up:
 *  the scoped light block applies in both schemes, the scoped dark block overrides it in dark,
 *  and anything a scope does not set falls back to the unscoped value for that scheme. */
function token(name: string, ctx: Context): string {
  const unscopedLight = theme?.decls.get(name);
  const unscopedDark = darkRoots.map((b) => b.decls.get(name)).find((v) => v !== undefined) ?? unscopedLight;
  const value = {
    light: unscopedLight,
    dark: unscopedDark,
    "after-hours light": scopedLight?.decls.get(name) ?? unscopedLight,
    "after-hours dark": scopedDark?.decls.get(name) ?? scopedLight?.decls.get(name) ?? unscopedDark,
  }[ctx];
  if (value === undefined) throw new Error(`index.css: ${name} has no value in ${ctx}`);
  return value;
}

/** A Sonner variable as index.css sets it (`var(--color-x)`), resolved in a context. */
function sonnerVar(name: string, ctx: Context): Rgb {
  const raw = toastRule?.decls.get(name);
  const m = raw && /^var\((--color-[a-z-]+)\)$/.exec(raw);
  if (!m) throw new Error(`index.css toast rule: ${name} should be var(--color-…), got "${raw}"`);
  return parseColour(token(m[1], ctx));
}

/** Sonner's own hard-coded description colour per scheme (not exposed as a variable). */
function sonnerDescription(scheme: "light" | "dark"): Rgb {
  const sonner = parseCssBlocks(sonnerCss);
  const header = scheme === "dark"
    ? "[data-sonner-toaster][data-sonner-theme='dark'] [data-description]"
    : "[data-sonner-toast][data-styled='true'] [data-description]";
  const value = sonner.find((b) => b.path[b.path.length - 1] === header)?.decls.get("color");
  if (!value) throw new Error(`sonner styles.css: no description colour for ${scheme}`);
  // Sonner writes hsl(0, 0%, 91%) with commas and #3f3f3f; normalise the comma form.
  return parseColour(value.replace(/hsl\(\s*([\d.]+),\s*([\d.]+%),\s*([\d.]+%)\s*\)/, "hsl($1 $2 $3)"));
}

const CONTEXTS: Context[] = ["light", "dark", "after-hours light", "after-hours dark"];

describe("toast theming (index.css → Sonner variables)", () => {
  it("sets Sonner's variables on a rule that out-ranks Sonner's own (0,2,0) theme rules", () => {
    expect(toastRule, `${TOAST_SELECTOR} block`).toBeDefined();
    // Three attribute/pseudo-class selectors: (0,3,0). Sonner's are (0,2,0) and its <style>
    // can load after index.css (production), so specificity has to settle the tie.
    expect(TOAST_SELECTOR.match(/\[|:root/g)).toHaveLength(3);
    for (const v of ["--normal-bg", "--normal-text", "--normal-border",
      "--success-bg", "--success-text", "--success-border", "--info-bg", "--info-text", "--info-border",
      "--warning-bg", "--warning-text", "--warning-border", "--error-bg", "--error-text", "--error-border"]) {
      expect(toastRule!.decls.get(v), v).toMatch(/^var\(--color-[a-z-]+\)$/);
    }
  });

  it("keeps Sonner's stylesheet unlayered (the reason Tailwind classes cannot style toasts)", () => {
    // If a Sonner upgrade ever layers its CSS, the classNames route becomes viable again and
    // this whole approach should be revisited — fail loudly rather than silently.
    expect(sonnerCss.length).toBeGreaterThan(1000);
    expect(sonnerCss).not.toMatch(/@layer/);
    expect(sonnerCss).toMatch(/background:\s*var\(--normal-bg\)/);
  });

  it("is reached by the After Hours scope (the toaster mounts outside the themed layout)", () => {
    for (const b of [scopedLight, scopedDark]) {
      expect(b?.path[b.path.length - 1]).toContain('body:has([data-theme="after-hours"]) [data-sonner-toaster]');
    }
  });

  describe.each(CONTEXTS)("%s — every toast pair reaches WCAG AA", (ctx) => {
    const bg = () => sonnerVar("--normal-bg", ctx);

    it("toast text on toast background: >= 4.5:1", () => {
      expect(contrast(sonnerVar("--normal-text", ctx), bg())).toBeGreaterThanOrEqual(4.5);
    });

    it("toast border against toast background (its only edge besides a 10% shadow): >= 3:1", () => {
      expect(contrast(sonnerVar("--normal-border", ctx), bg())).toBeGreaterThanOrEqual(3);
    });

    it.each(["success", "info", "warning", "error"])("%s text >= 4.5:1 and border >= 3:1 on its background", (kind) => {
      const kindBg = sonnerVar(`--${kind}-bg`, ctx);
      expect(contrast(sonnerVar(`--${kind}-text`, ctx), kindBg), `${kind} text`).toBeGreaterThanOrEqual(4.5);
      expect(contrast(sonnerVar(`--${kind}-border`, ctx), kindBg), `${kind} border`).toBeGreaterThanOrEqual(3);
    });

    it("Sonner's own description colour on the toast background: >= 4.5:1", () => {
      const scheme = ctx.endsWith("dark") ? "dark" : "light";
      expect(contrast(sonnerDescription(scheme), bg())).toBeGreaterThanOrEqual(4.5);
    });
  });

  it("paints out of scope with today's unscoped surface (white / near-black), not Sonner's stock #fff / #000", () => {
    expect(sonnerVar("--normal-bg", "light")).toEqual([255, 255, 255]);
    expect(sonnerVar("--normal-bg", "dark")).toEqual([9, 9, 11]);
    expect(sonnerVar("--normal-text", "light")).toEqual([9, 9, 11]);
    expect(sonnerVar("--normal-bg", "after-hours light")).toEqual([0xff, 0xfd, 0xf7]);
    expect(sonnerVar("--normal-bg", "after-hours dark")).toEqual([0x21, 0x1d, 0x26]);
  });
});
