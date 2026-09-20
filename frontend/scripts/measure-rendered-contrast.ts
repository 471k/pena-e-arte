/**
 * Measures the WCAG contrast ratio of what the browser actually RENDERS for a fixed list of UI
 * elements (sidebar group labels, tabs, badges, helper text, placeholders, disabled buttons, ...),
 * in both the light and dark themes. Unlike check-contrast.ts — which checks hand-copied design
 * tokens against each other — this reads computed styles off real pages, so it catches a token
 * being used on a background it was never meant for.
 *
 * REPORT ONLY: it prints a markdown table and always exits 0. It is deliberately not a CI gate:
 * no token change is being validated against it yet (see docs/claude/contrast-audit-2026-09-20.md).
 * Once the token-level fix PR exists, turn the `Fail` rows into assertions.
 *
 * Usage (needs the Vite dev server on http://localhost:5173 — `pnpm dev` — no backend; API calls are
 * mocked the same way the Playwright e2e specs mock them):
 *
 *     node scripts/measure-rendered-contrast.ts
 *
 * How a ratio is measured: the element's computed `color` and the first non-transparent ancestor
 * `background-color` (alpha composited over what's behind it) are normalised to sRGB through a
 * 1x1 canvas — Tailwind v4 emits oklch()/color-mix() values a plain rgb() parser can't read — then
 * fed to the WCAG 2.x relative-luminance formula. Thresholds: 4.5:1 for normal text, 3:1 for large
 * text (>= 24px, or >= 18.66px bold).
 */
import { chromium, type Browser, type Page } from "@playwright/test";
import {
  ARTIST_ID, STUDIO_ID, OWNER_TOKEN,
  mockApiFallback, loginAs,
} from "../e2e/helpers.ts";

const BASE_URL = process.env.PLAYWRIGHT_BASE_URL ?? "http://localhost:5173";

type Rgb = [number, number, number];

interface Sample {
  text: string;
  fontSizePx: number;
  fontWeight: number;
  fg: Rgb;
  bg: Rgb;
}

interface Target {
  /** Human label for the report. */
  name: string;
  /** Page the target lives on. */
  page: "artist-social" | "artist-profile";
  selector: string;
  /** Measure a pseudo-element's colour instead (e.g. "::placeholder"). */
  pseudo?: string;
  /**
   * WCAG 1.4.11 boundary check: compare the element's own background against the background right
   * behind it (its parent's), instead of its text against its background. Only a failure when the
   * fill is the SOLE indicator of the state it shows.
   */
  boundary?: boolean;
  /** Prepare the page before measuring (open a dialog, ...). */
  prepare?: (page: Page) => Promise<void>;
}

const ARTIST = {
  id: ARTIST_ID,
  studioId: STUDIO_ID,
  firstName: "Rafaela",
  lastName: "Costa",
  email: "rafaela@tinta-alma.com",
  specializations: ["fineline"],
  hourlyRate: 80,
  isActive: true,
  avatarUrl: null,
  portfolioImages: [],
  slug: "rafaela-costa",
  userId: "user-id-artist",
  createdAt: "2024-01-15T10:00:00.000Z",
  updatedAt: "2024-06-01T10:00:00.000Z",
};

const SOCIAL = ["Instagram", "TikTok", "Facebook", "X", "YouTube"].map((platform) => ({
  platform,
  handle: platform === "Facebook" ? "rafaink" : platform === "TikTok" ? "rafa.ink" : null,
  isVerified: platform === "TikTok",
  verifiedAt: null,
  verificationMethod: null,
  isOAuthConfigured: platform === "Instagram" || platform === "TikTok",
  isManualCheckSupported: platform !== "TikTok",
  hasPendingCode: false,
  pendingCodeExpiresAt: null,
}));

const TARGETS: Target[] = [
  { name: "Sidebar group label", page: "artist-profile", selector: "aside nav p.text-xs" },
  { name: "Sidebar item (inactive)", page: "artist-profile", selector: "aside nav a:not([aria-current])" },
  { name: "Sidebar item (active)", page: "artist-profile", selector: "aside nav a[aria-current=\"page\"]" },
  { name: "Sidebar active fill vs page (boundary, 1.4.11)", page: "artist-profile", selector: "aside nav a[aria-current=\"page\"]", boundary: true },
  { name: "Collapse-shortcut chip (Ctrl B)", page: "artist-profile", selector: "aside kbd" },
  { name: "Header user subtitle (role)", page: "artist-profile", selector: "header button[aria-label=\"User menu\"] span.text-xs" },
  { name: "Page subtitle (\"Artist\")", page: "artist-profile", selector: "main h1 + p" },
  { name: "Tab trigger (inactive)", page: "artist-profile", selector: "[role=\"tab\"][data-state=\"inactive\"]" },
  { name: "Tab trigger (active)", page: "artist-profile", selector: "[role=\"tab\"][data-state=\"active\"]" },
  { name: "Section helper text", page: "artist-social", selector: "#social-heading + p" },
  { name: "Row helper / secondary line", page: "artist-social", selector: "ul[aria-label=\"Connected accounts\"] li p.text-xs.text-muted-foreground" },
  { name: "Row handle (@handle)", page: "artist-social", selector: "ul[aria-label=\"Connected accounts\"] li p.break-all" },
  { name: "Status badge (Not linked)", page: "artist-social", selector: "ul[aria-label=\"Connected accounts\"] li div.rounded-full.border" },
  { name: "Verified badge", page: "artist-social", selector: "ul[aria-label=\"Connected accounts\"] li span.rounded-full" },
  { name: "Handle input placeholder", page: "artist-social", selector: "input[placeholder=\"handle\"]", pseudo: "::placeholder" },
  { name: "Handle input text", page: "artist-social", selector: "input[aria-label=\"Facebook handle\"]" },
  { name: "Disabled button (Get verification code)", page: "artist-social", selector: "button:disabled" },
  { name: "Ghost destructive button (Disconnect)", page: "artist-social", selector: "button[aria-label=\"Disconnect TikTok\"]" },
  {
    name: "Destructive button in dialog (Disconnect)",
    page: "artist-social",
    selector: "[role=\"dialog\"] button.bg-destructive",
    prepare: async (page) => {
      await page.getByRole("button", { name: "Disconnect TikTok" }).click();
      await page.getByRole("dialog").waitFor();
    },
  },
];

function srgbToLinear(c: number): number {
  const s = c / 255;
  return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
}

function luminance([r, g, b]: Rgb): number {
  return 0.2126 * srgbToLinear(r) + 0.7152 * srgbToLinear(g) + 0.0722 * srgbToLinear(b);
}

function ratio(a: Rgb, b: Rgb): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

/** Runs in the browser. Returns null when nothing matches or the element isn't visible. */
async function sample(page: Page, target: Target): Promise<Sample | null> {
  return page.evaluate(({ selector, pseudo, boundary }) => {
    const el = Array.from(document.querySelectorAll<HTMLElement>(selector)).find((candidate) => {
      const rect = candidate.getBoundingClientRect();
      const visible = rect.width > 0 && rect.height > 0;
      const hasText = (candidate.textContent ?? "").trim() !== "" || candidate instanceof HTMLInputElement;
      return visible && hasText;
    });
    if (!el) return null;

    const canvas = document.createElement("canvas");
    canvas.width = 1;
    canvas.height = 1;
    const ctx = canvas.getContext("2d", { willReadFrequently: true })!;

    // Normalises any CSS colour (oklch, color-mix, rgb, ...) to sRGBA through the canvas.
    function toRgba(css: string): [number, number, number, number] {
      ctx.clearRect(0, 0, 1, 1);
      ctx.fillStyle = "#000";
      ctx.fillStyle = css;
      ctx.fillRect(0, 0, 1, 1);
      const [r, g, b, a] = ctx.getImageData(0, 0, 1, 1).data;
      return [r, g, b, a / 255];
    }

    const style = getComputedStyle(el, pseudo ?? null);
    const fgRaw = toRgba(style.color);

    // Effective background: composite each ancestor's background over the next, until opaque.
    function effectiveBackground(start: HTMLElement): [number, number, number] {
      const layers: Array<[number, number, number, number]> = [];
      for (let node: HTMLElement | null = start; node; node = node.parentElement) {
        const bg = toRgba(getComputedStyle(node).backgroundColor);
        if (bg[3] > 0) layers.push(bg);
        if (bg[3] >= 1) break;
      }
      let result: [number, number, number] = [255, 255, 255];
      if (layers.length && layers[layers.length - 1][3] < 1) {
        // Reached <html> without an opaque layer: fall back to the page's canvas colour.
        const canvasColour = toRgba(getComputedStyle(document.documentElement).backgroundColor);
        result = canvasColour[3] > 0 ? [canvasColour[0], canvasColour[1], canvasColour[2]] : [255, 255, 255];
      }
      for (let i = layers.length - 1; i >= 0; i--) {
        const [r, g, b, a] = layers[i];
        result = [r * a + result[0] * (1 - a), g * a + result[1] * (1 - a), b * a + result[2] * (1 - a)];
      }
      return result;
    }
    const base = effectiveBackground(el);

    if (boundary) {
      return {
        text: (el.textContent ?? "").trim().slice(0, 40),
        fontSizePx: parseFloat(style.fontSize),
        fontWeight: Number(style.fontWeight) || 400,
        fg: base,
        bg: effectiveBackground(el.parentElement ?? el),
      };
    }

    // Foreground alpha (e.g. text-muted-foreground/70): composite over the resolved background.
    const fg: [number, number, number] = [
      fgRaw[0] * fgRaw[3] + base[0] * (1 - fgRaw[3]),
      fgRaw[1] * fgRaw[3] + base[1] * (1 - fgRaw[3]),
      fgRaw[2] * fgRaw[3] + base[2] * (1 - fgRaw[3]),
    ];

    const opacity = Number(style.opacity);
    const effectiveFg: [number, number, number] = opacity < 1
      ? [fg[0] * opacity + base[0] * (1 - opacity), fg[1] * opacity + base[1] * (1 - opacity), fg[2] * opacity + base[2] * (1 - opacity)]
      : fg;

    return {
      text: (el.textContent ?? el.getAttribute("placeholder") ?? "").trim().slice(0, 40),
      fontSizePx: parseFloat(style.fontSize),
      fontWeight: Number(style.fontWeight) || 400,
      fg: effectiveFg,
      bg: base,
    };
  }, { selector: target.selector, pseudo: target.pseudo, boundary: target.boundary === true });
}

async function mockAll(page: Page): Promise<void> {
  await mockApiFallback(page);
  const json = (body: unknown) => ({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  await page.route(`**/api/v1/artists/${ARTIST_ID}`, (route) => route.fulfill(json(ARTIST)));
  await page.route("**/api/v1/artists/me", (route) => route.fulfill(json(ARTIST)));
  await page.route(`**/api/v1/artists/${ARTIST_ID}/schedule`, (route) => route.fulfill(json({ schedule: [], timeOff: [] })));
  await page.route(`**/api/v1/artists/${ARTIST_ID}/instagram/status`, (route) =>
    route.fulfill(json({ isConnected: false, username: null, lastSyncedAt: null, postCount: 0 })));
  await page.route(`**/api/v1/artists/${ARTIST_ID}/social`, (route) => route.fulfill(json(SOCIAL)));
}

interface Row {
  theme: "light" | "dark";
  name: string;
  text: string;
  size: string;
  ratio: number | null;
  required: number;
  result: "Pass" | "Fail" | "Not found";
}

async function measureTheme(browser: Browser, theme: "light" | "dark"): Promise<Row[]> {
  const context = await browser.newContext({ baseURL: BASE_URL, colorScheme: theme, viewport: { width: 1280, height: 900 } });
  const page = await context.newPage();
  await mockAll(page);
  await loginAs(page, OWNER_TOKEN, "owner@tinta-alma.com", /^(?!.*\/login).+/);

  const rows: Row[] = [];
  for (const pageName of ["artist-profile", "artist-social"] as const) {
    await page.goto(`/artists/${ARTIST_ID}${pageName === "artist-social" ? "?tab=social" : ""}`);
    await page.getByRole("heading", { level: 1, name: "Rafaela Costa" }).waitFor();
    if (pageName === "artist-social") await page.getByRole("list", { name: "Connected accounts" }).waitFor();
    await page.waitForTimeout(300);

    for (const target of TARGETS.filter((t) => t.page === pageName)) {
      if (target.prepare) await target.prepare(page);
      const s = await sample(page, target);
      if (!s) {
        rows.push({ theme, name: target.name, text: "", size: "", ratio: null, required: 4.5, result: "Not found" });
        continue;
      }
      const large = s.fontSizePx >= 24 || (s.fontSizePx >= 18.66 && s.fontWeight >= 700);
      const required = target.boundary || large ? 3 : 4.5;
      const r = ratio(s.fg, s.bg);
      rows.push({
        theme, name: target.name, text: s.text,
        size: `${Math.round(s.fontSizePx * 10) / 10}px/${s.fontWeight}`,
        ratio: Math.round(r * 100) / 100, required,
        result: r >= required ? "Pass" : "Fail",
      });
    }
  }
  await context.close();
  return rows;
}

async function main(): Promise<void> {
  const browser = await chromium.launch();
  const rows: Row[] = [];
  try {
    for (const theme of ["light", "dark"] as const) rows.push(...(await measureTheme(browser, theme)));
  } finally {
    await browser.close();
  }

  console.log("| Theme | Element | Sample text | Size/weight | Ratio | Required | Result |");
  console.log("|---|---|---|---|---|---|---|");
  for (const r of rows) {
    console.log(
      `| ${r.theme} | ${r.name} | ${r.text.replaceAll("|", "\\|")} | ${r.size} | ${r.ratio === null ? "-" : `${r.ratio}:1`} | ${r.required}:1 | ${r.result} |`,
    );
  }
  const failures = rows.filter((r) => r.result === "Fail").length;
  console.log(`\n${rows.length} measurements, ${failures} below threshold, ${rows.filter((r) => r.result === "Not found").length} not found.`);
}

await main();
