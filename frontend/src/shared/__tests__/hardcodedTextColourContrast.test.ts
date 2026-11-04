import { describe, it, expect } from "vitest";
import tailwindTheme from "tailwindcss/theme.css?raw";
import indexCss from "../../index.css?raw";
import { contrast, over, parseColour, parseCssBlocks, parseTailwindPalette, type Rgb } from "@/test/contrast";

// Guard (accessibility cleanup, 2026-11-04): stop light-mode hard-coded colour text from
// regrowing below WCAG AA.
//
// Before this guard, 119 light-mode uses of hard-coded Tailwind text colours (text-green-500,
// text-amber-600, text-blue-500, …) measured under 4.5:1 on the backgrounds they render on —
// mostly status text written against white that never met AA. They now use the semantic
// tokens (text-success-text / -warning-text / -destructive-text / -info-text) or a measured
// darker shade. This test re-measures every remaining one on every build.
//
// What it checks: every Tailwind palette text class in non-test source under src/ that is
// NOT behind a `dark:` variant (so it paints in light mode — hover:/group-hover: states
// included). Each one is measured against what it sits on:
//   • a palette background on the same line (e.g. `bg-green-500/10 text-…`), composited over
//     the page, or
//   • the light page backgrounds: white (public pages) and the After Hours canvas and
//     surface (authenticated screens), read from index.css.
// It must reach 4.5:1 — the WCAG text bar, which is also comfortably above the 3:1 bar for
// meaningful icons. The worst background wins.
//
// Two ways out, both deliberate:
//   1. `aria-hidden="true"` on the same line: a purely decorative element, the meaning carried
//      by adjacent text (e.g. the large tick above "Payment received"). WCAG sets no contrast
//      requirement for decoration. Making it explicit in the markup is the point.
//   2. ALLOWED below, entry by entry, with the reason. Keep it short.
//
// `dark:` twins are out of scope here: they paint on a dark canvas and were measured with it.

const palette = parseTailwindPalette(tailwindTheme);
const blocks = parseCssBlocks(indexCss);
const themeBlock = blocks.find((b) => b.path.length === 1 && b.path[0] === "@theme");
const scopeBlock = blocks.find((b) => b.path.length === 1 && b.path[0].startsWith('[data-theme="after-hours"]'));

function must(value: string | undefined, what: string): string {
  if (value === undefined) throw new Error(`index.css: ${what} not found`);
  return value;
}

const LIGHT_PAGES: Record<string, Rgb> = {
  "page (white)": parseColour(must(themeBlock?.decls.get("--color-background"), "@theme --color-background")),
  "After Hours canvas": parseColour(must(scopeBlock?.decls.get("--color-background"), "scoped --color-background")),
  "After Hours surface": parseColour(must(scopeBlock?.decls.get("--color-card"), "scoped --color-card")),
};

const ALLOWED: { file: string; classes: string[]; reason: string }[] = [
  ...[
    "features/deposit-rules/components/DepositRuleCard.tsx",
    "features/deposit-rules/components/DepositRuleDetailPage.tsx",
    "features/promo-codes/components/PromoCodeCard.tsx",
    "features/promo-codes/components/PromoCodeDetailPage.tsx",
  ].map((file) => ({
    file,
    classes: ["text-blue-600", "text-purple-600"],
    reason: "Icon-only round badge ($ or %) beside the rule's name; the type is spelled out as text " +
      "next to it (\"Fixed\" / \"Percent\"), and the glyph is aria-hidden. Decorative.",
  })),
  ...[
    "features/saved-payment-methods/components/SavedPaymentMethodsPage.tsx",
    "features/services/components/ServiceCard.tsx",
    "features/services/components/ServiceDetailPage.tsx",
  ].map((file) => ({
    file,
    classes: ["text-blue-600"],
    reason: "Icon-only round badge beside the item's name, carrying no state; the glyph is aria-hidden. Decorative.",
  })),
  {
    file: "shared/components/ui/StarRating.tsx",
    classes: ["text-amber-400"],
    reason: "Amber stars are the category convention. The rating is never carried by the stars alone: " +
      "the display wrapper is role=\"img\" with aria-label \"Rating: N out of 5 stars\", and each " +
      "interactive star is a labelled radio with a live text readout. Stars themselves are aria-hidden.",
  },
];

const TEXT_RE = /(?<![\w-])((?:[a-z0-9-]+:)*)text-([a-z]+-\d{2,3})(?:\/(\d+))?(?![\w-])/g;
const BG_RE = /(?<![\w-])((?:[a-z0-9-]+:)*)bg-([a-z]+-\d{2,3})(?:\/(\d+))?(?![\w-])/g;

type Finding = { where: string; cls: string; ratio: number; on: string };

/** Measures every light-mode palette text class in `source`; returns the ones under 4.5:1. */
function measureSource(file: string, source: string): Finding[] {
  const findings: Finding[] = [];
  source.split(/\r?\n/).forEach((line, i) => {
    if (line.includes('aria-hidden="true"')) return;
    // Palette backgrounds that paint in light mode, at rest, on this line.
    const bgs: { name: string; rgb: Rgb; alpha: number }[] = [];
    for (const m of line.matchAll(BG_RE)) {
      const variants = m[1].split(":");
      if (variants.some((v) => ["dark", "hover", "focus", "active", "group-hover"].includes(v))) continue;
      if (palette[m[2]]) bgs.push({ name: m[0], rgb: palette[m[2]], alpha: m[3] ? Number(m[3]) / 100 : 1 });
    }
    for (const m of line.matchAll(TEXT_RE)) {
      const [cls, variants, name, alphaPct] = m;
      if (variants.split(":").includes("dark") || !palette[name]) continue;
      let worst: { ratio: number; on: string } = { ratio: Infinity, on: "" };
      for (const [pageName, page] of Object.entries(LIGHT_PAGES)) {
        const backdrops = bgs.length
          ? bgs.map((b) => ({ rgb: over(b.rgb, b.alpha, page), on: `${b.name} over ${pageName}` }))
          : [{ rgb: page, on: pageName }];
        for (const b of backdrops) {
          const fg = alphaPct ? over(palette[name], Number(alphaPct) / 100, b.rgb) : palette[name];
          const r = contrast(fg, b.rgb);
          if (r < worst.ratio) worst = { ratio: r, on: b.on };
        }
      }
      if (worst.ratio < 4.5) findings.push({ where: `${file}:${i + 1}`, cls, ...worst });
    }
  });
  return findings;
}

const sources = import.meta.glob<string>(
  ["/src/**/*.{ts,tsx}", "!/src/**/__tests__/**", "!/src/**/*.test.{ts,tsx}", "!/src/test/**"],
  { query: "?raw", import: "default", eager: true },
);

describe("hard-coded light-mode text colours meet WCAG AA", () => {
  const all = Object.entries(sources).flatMap(([path, src]) => measureSource(path.replace(/^\/src\//, ""), src));
  const isAllowed = (f: Finding) =>
    ALLOWED.some((a) => f.where.startsWith(`${a.file}:`) && a.classes.includes(f.cls.replace(/^(?:[a-z0-9-]+:)+/, "")));

  it("reads the source tree and the Tailwind palette (guards against a vacuous pass)", () => {
    expect(Object.keys(sources).length).toBeGreaterThan(300);
    expect(palette["green-600"]).toBeDefined();
    expect(Object.keys(palette).length).toBeGreaterThan(200);
  });

  it("has no light-mode palette text class under 4.5:1 outside the justified allow-list", () => {
    const offenders = all.filter((f) => !isAllowed(f))
      .map((f) => `${f.where}  ${f.cls}  ${f.ratio.toFixed(2)}:1 on ${f.on}`);
    expect(offenders, "Use a semantic token (text-success-text, text-warning-text, text-destructive-text, " +
      "text-info-text) or a measured darker shade; mark purely decorative elements aria-hidden=\"true\"; " +
      "only then consider ALLOWED.\n" + offenders.join("\n")).toEqual([]);
  });

  it("has no stale allow-list entries", () => {
    for (const a of ALLOWED) {
      for (const cls of a.classes) {
        expect(all.some((f) => f.where.startsWith(`${a.file}:`) && f.cls === cls), `${a.file} ${cls}`).toBe(true);
      }
    }
  });

  it("would catch a regression: the classes this cleanup removed all fail when reintroduced", () => {
    const sample = [
      '<p className="text-xs text-green-600 dark:text-green-400">Paid</p>',
      '<span className="bg-amber-500/15 text-amber-600">Reviewing</span>',
      '<p className="text-sm text-green-400">Review submitted</p>',
      '<AlertTriangle className="h-4 w-4 text-amber-500" />',
      '<p className="text-xs text-blue-500">Info</p>',
      '<button className="text-red-500 hover:text-red-600">Delete</button>',
    ].join("\n");
    const caught = measureSource("sample.tsx", sample).map((f) => f.cls);
    expect(caught).toEqual(["text-green-600", "text-amber-600", "text-green-400", "text-amber-500", "text-blue-500", "text-red-500", "hover:text-red-600"]);
    // …and the replacements pass: tokens are not palette classes, darker shades measure clean.
    expect(measureSource("sample.tsx", '<span className="bg-green-500/10 text-success-text">Paid</span>')).toEqual([]);
    expect(measureSource("sample.tsx", '<span className="bg-purple-500/15 text-purple-700 dark:text-purple-300">x</span>')).toEqual([]);
    expect(measureSource("sample.tsx", '<CheckCircle aria-hidden="true" className="h-8 w-8 text-green-500" />')).toEqual([]);
  });
});
