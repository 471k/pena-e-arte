import { describe, it, expect } from "vitest";
// `?raw` (declared by vite/client) returns the stylesheet's source text, untransformed.
import indexCss from "../../index.css?raw";

// Contract + contrast tests for the scoped After Hours palette (DEC01, slice 0).
//
// jsdom cannot evaluate `prefers-color-scheme` or resolve the cascade, so the real-browser
// proof lives in e2e/accessibility.spec.ts (both colour schemes, axe-core). This file pins
// what that cannot: that every token outside the scope kept its value, that the scope is
// plain attribute-selector CSS (never a nested `@theme`, see the comment above index.css's
// first dark block), and the WCAG ratios of every semantic pair, recomputed from the hex
// values in the stylesheet itself — so a later edit to a token cannot silently drop a pair
// below AA.

// ── Tiny block parser (comments stripped, braces matched) ──────────────────────────────

const norm = (s: string) => s.replace(/\s+/g, " ").trim();

type Block = { path: string[]; decls: Map<string, string> };

function parseBlocks(css: string): Block[] {
  const src = css.replace(/\/\*[\s\S]*?\*\//g, "");
  const blocks: Block[] = [];
  const stack: string[] = [];
  let buf = "";
  for (const ch of src) {
    if (ch === "{") {
      // The header is whatever follows the last statement (e.g. `@import "tailwindcss";`).
      stack.push(norm(buf.slice(buf.lastIndexOf(";") + 1)));
      buf = "";
    } else if (ch === "}") {
      const header = stack.pop();
      if (header === undefined) throw new Error("unbalanced braces in index.css");
      // Declarations written directly inside this block (after its last nested block, if
      // any — none of the blocks these tests read mix the two).
      const decls = new Map<string, string>();
      for (const part of buf.split(";")) {
        const i = part.indexOf(":");
        if (i > 0) decls.set(part.slice(0, i).trim(), norm(part.slice(i + 1)));
      }
      blocks.push({ path: [...stack, header], decls });
      buf = "";
    } else {
      buf += ch;
    }
  }
  return blocks;
}

const blocks = parseBlocks(indexCss);
const DARK = "@media (prefers-color-scheme: dark)";

const theme = blocks.find((b) => b.path.length === 1 && b.path[0] === "@theme");
const darkRootBlocks = blocks.filter((b) => b.path.length === 2 && b.path[0] === DARK && b.path[1] === ":root");
const isScopeHeader = (h: string) => norm(h).startsWith('[data-theme="after-hours"]');
const scopedLight = blocks.find((b) => b.path.length === 1 && isScopeHeader(b.path[0]));
const scopedDark = blocks.find((b) => b.path.length === 2 && b.path[0] === DARK && isScopeHeader(b.path[1]));

function must<T>(v: T | undefined, what: string): T {
  if (v === undefined) throw new Error(`index.css: ${what} not found`);
  return v;
}

// ── WCAG 2.1 contrast (https://www.w3.org/TR/WCAG21/#dfn-relative-luminance) ───────────

type Rgb = [number, number, number];

function hexToRgb(value: string): Rgb {
  const m = /^#([0-9a-f]{6})$/i.exec(value.trim());
  if (!m) throw new Error(`expected a #RRGGBB value, got "${value}"`);
  const n = parseInt(m[1], 16);
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}
const channel = (c: number) => { const s = c / 255; return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4; };
const luminance = ([r, g, b]: Rgb) => 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
function contrast(a: Rgb, b: Rgb): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}
/** A translucent layer (e.g. primary at Tailwind's 90% opacity modifier) painted over an opaque backdrop. */
const over = (fg: Rgb, alpha: number, bg: Rgb): Rgb =>
  [0, 1, 2].map((i) => Math.round(fg[i] * alpha + bg[i] * (1 - alpha))) as Rgb;

// ── Tests ──────────────────────────────────────────────────────────────────────────────

describe("After Hours theme layer (index.css)", () => {
  it("leaves every pre-existing unscoped light token at its original value", () => {
    const t = must(theme, "@theme block").decls;
    const original: Record<string, string> = {
      "--color-background": "hsl(0 0% 100%)", "--color-foreground": "hsl(240 10% 3.9%)",
      "--color-card": "hsl(0 0% 100%)", "--color-card-foreground": "hsl(240 10% 3.9%)",
      "--color-popover": "hsl(0 0% 100%)", "--color-popover-foreground": "hsl(240 10% 3.9%)",
      "--color-primary": "hsl(240 5.9% 10%)", "--color-primary-foreground": "hsl(0 0% 98%)",
      "--color-secondary": "hsl(240 4.8% 95.9%)", "--color-secondary-foreground": "hsl(240 5.9% 10%)",
      "--color-muted": "hsl(240 4.8% 95.9%)", "--color-muted-foreground": "hsl(240 3.8% 42%)",
      "--color-accent": "hsl(240 4.8% 95.9%)", "--color-accent-foreground": "hsl(240 5.9% 10%)",
      "--color-destructive": "hsl(0 84.2% 45%)", "--color-destructive-foreground": "hsl(0 0% 98%)",
      "--color-destructive-text": "hsl(0 74% 42%)", "--color-border": "hsl(240 5.9% 58%)",
      "--color-input": "hsl(240 5.9% 58%)", "--color-ring": "hsl(240 5.9% 10%)",
      "--radius": "0.5rem", "--font-sans": '"Fraunces", sans-serif',
    };
    for (const [token, value] of Object.entries(original)) expect(t.get(token), token).toBe(value);
  });

  it("leaves the original unscoped dark block's declarations exactly as they were", () => {
    const original = must(darkRootBlocks[0], "first dark :root block").decls;
    expect(Object.fromEntries(original)).toEqual({
      "--color-background": "hsl(240 10% 3.9%)", "--color-foreground": "hsl(0 0% 98%)",
      "--color-card": "hsl(240 10% 3.9%)", "--color-card-foreground": "hsl(0 0% 98%)",
      "--color-popover": "hsl(240 10% 3.9%)", "--color-popover-foreground": "hsl(0 0% 98%)",
      "--color-primary": "hsl(0 0% 98%)", "--color-primary-foreground": "hsl(240 5.9% 10%)",
      "--color-secondary": "hsl(240 3.7% 15.9%)", "--color-secondary-foreground": "hsl(0 0% 98%)",
      "--color-muted": "hsl(240 3.7% 15.9%)", "--color-muted-foreground": "hsl(240 5% 64.9%)",
      "--color-accent": "hsl(240 3.7% 15.9%)", "--color-accent-foreground": "hsl(0 0% 98%)",
      "--color-destructive": "hsl(0 62.8% 30.6%)", "--color-destructive-foreground": "hsl(0 0% 98%)",
      "--color-destructive-text": "hsl(0 90% 65%)", "--color-border": "hsl(240 5% 40%)",
      "--color-input": "hsl(240 5% 40%)", "--color-ring": "hsl(240 4.9% 83.9%)",
    });
  });

  it("registers each new token in @theme (so its utility exists) with an unscoped default that matches the old look", () => {
    const t = must(theme, "@theme block").decls;
    // text-link outside the scope must look exactly like the old text-primary,
    // and hover:bg-primary-hover exactly like the old 90%-opacity primary hover.
    expect(t.get("--color-link")).toBe(t.get("--color-primary"));
    expect(t.get("--color-primary-hover")).toBe("color-mix(in oklab, var(--color-primary) 90%, transparent)");
    for (const token of ["--color-rule", "--color-success-text", "--color-warning-text"]) {
      expect(t.has(token), token).toBe(true);
    }
    const newDark = must(darkRootBlocks[1], "dark :root block for the new tokens").decls;
    expect(newDark.get("--color-link")).toBe(darkRootBlocks[0].decls.get("--color-primary"));
  });

  it("is plain attribute-scoped CSS with a light set and a prefers-color-scheme dark set — never a nested @theme", () => {
    expect(scopedLight, "top-level [data-theme=\"after-hours\"] block").toBeDefined();
    expect(scopedDark, "[data-theme=\"after-hours\"] block inside the dark media query").toBeDefined();
    expect(blocks.some((b) => b.path.length > 1 && b.path.includes("@theme"))).toBe(false);
    // No forced theme on the document: nothing scoped to html/body themselves.
    for (const b of [scopedLight, scopedDark]) {
      const selectors = norm(must(b, "scope").path[b!.path.length - 1]).split(",").map((s) => s.trim());
      for (const s of selectors) expect(s).not.toMatch(/^(html|body|:root)$/);
    }
  });

  it("keeps the decorative rule colour off the control boundary in both modes", () => {
    for (const b of [must(scopedLight, "light scope"), must(scopedDark, "dark scope")]) {
      expect(b.decls.get("--color-border")).not.toBe(b.decls.get("--color-rule"));
      expect(b.decls.get("--color-input")).toBe(b.decls.get("--color-border"));
    }
  });

  it("does not touch fonts or the destructive fill pair", () => {
    expect(norm(indexCss)).toContain('font-family: "Fraunces", sans-serif;');
    for (const b of [must(scopedLight, "light scope"), must(scopedDark, "dark scope")]) {
      for (const token of ["--font-sans", "--color-destructive", "--color-destructive-foreground"]) {
        expect(b.decls.has(token), token).toBe(false);
      }
    }
  });

  describe.each([
    ["light", () => must(scopedLight, "light scope")],
    ["dark", () => must(scopedDark, "dark scope")],
  ])("%s mode — every meaningful pair reaches WCAG AA", (_mode, get) => {
    const c = (token: string) => hexToRgb(must(get().decls.get(token), token));
    const canvas = () => c("--color-background");
    const surface = () => c("--color-card");
    const backdrops = () => [["canvas", canvas()], ["surface", surface()]] as const;

    it("body text, muted text, link, error/success/warning text: >= 4.5:1 on canvas and surface", () => {
      for (const [name, bg] of backdrops()) {
        for (const token of ["--color-foreground", "--color-muted-foreground", "--color-link",
          "--color-destructive-text", "--color-success-text", "--color-warning-text"]) {
          expect(contrast(c(token), bg), `${token} on ${name}`).toBeGreaterThanOrEqual(4.5);
        }
      }
      expect(contrast(c("--color-popover-foreground"), c("--color-popover"))).toBeGreaterThanOrEqual(4.5);
      expect(contrast(c("--color-card-foreground"), c("--color-card"))).toBeGreaterThanOrEqual(4.5);
    });

    it("muted / secondary / accent surfaces keep their text >= 4.5:1", () => {
      for (const bg of ["--color-muted", "--color-secondary", "--color-accent"]) {
        expect(contrast(c("--color-muted-foreground"), c(bg)), `muted text on ${bg}`).toBeGreaterThanOrEqual(4.5);
        expect(contrast(c("--color-link"), c(bg)), `link on ${bg}`).toBeGreaterThanOrEqual(4.5);
      }
      expect(contrast(c("--color-secondary-foreground"), c("--color-secondary"))).toBeGreaterThanOrEqual(4.5);
      expect(contrast(c("--color-accent-foreground"), c("--color-accent"))).toBeGreaterThanOrEqual(4.5);
    });

    it("white on the primary fill, its hover token, and the translucent hovers still in use: >= 4.5:1", () => {
      const on = c("--color-primary-foreground");
      expect(contrast(on, c("--color-primary"))).toBeGreaterThanOrEqual(4.5);
      expect(contrast(on, c("--color-primary-hover"))).toBeGreaterThanOrEqual(4.5);
      // badge.tsx's hover:bg-primary/80 and any remaining /90 — painted over the page.
      for (const alpha of [0.9, 0.8]) {
        for (const [name, bg] of backdrops()) {
          expect(contrast(on, over(c("--color-primary"), alpha, bg)), `white on primary/${alpha * 100} over ${name}`)
            .toBeGreaterThanOrEqual(4.5);
        }
      }
    });

    it("link text on the tinted primary backgrounds (sidebar active item, selected cards): >= 4.5:1", () => {
      for (const alpha of [0.1, 0.05]) {
        for (const [name, bg] of backdrops()) {
          expect(contrast(c("--color-link"), over(c("--color-primary"), alpha, bg)), `link on primary/${alpha * 100} over ${name}`)
            .toBeGreaterThanOrEqual(4.5);
        }
      }
    });

    it("focus ring, control boundary and primary fill (non-text): >= 3:1 against canvas and surface", () => {
      for (const [name, bg] of backdrops()) {
        for (const token of ["--color-ring", "--color-border", "--color-input", "--color-primary"]) {
          expect(contrast(c(token), bg), `${token} vs ${name}`).toBeGreaterThanOrEqual(3);
        }
      }
      expect(contrast(c("--color-border"), c("--color-muted")), "border vs muted").toBeGreaterThanOrEqual(3);
    });
  });
});
