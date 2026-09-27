/**
 * Generates frontend/public/og-image.png (1200x630), the default social-preview image referenced by
 * index.html and the per-route metadata (https://tattooos.co/og-image.png).
 *
 * THIS IS A PLACEHOLDER brand asset: wordmark + tagline on a dark background, nothing more. Replace
 * frontend/public/og-image.png with real artwork when marketing supplies it (keep it 1200x630 and
 * under ~300 KB). Run manually — not part of `pnpm build`:
 *
 *   node scripts/generate-og-image.mjs
 *
 * Uses the Chromium that @playwright/test already installs (no new dependency).
 */
import { chromium } from "@playwright/test";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const TAGLINE = "Booking & studio management for tattoo shops";
const SUBLINE = "Online booking · Deposits · Digital consent forms";

const html = `<!doctype html>
<html><head><meta charset="utf-8" /><style>
  html, body { margin: 0; width: 1200px; height: 630px; }
  body {
    display: flex; flex-direction: column; justify-content: center; padding: 0 96px; box-sizing: border-box;
    background: radial-gradient(1100px 520px at 88% -10%, #3b1d7a 0%, rgba(59,29,122,0) 60%), #0b0b0f;
    color: #f5f3ff; font-family: Georgia, "Times New Roman", serif;
  }
  .bar { width: 96px; height: 8px; border-radius: 4px; background: #863bff; margin-bottom: 44px; }
  .brand { font-size: 132px; font-weight: 700; letter-spacing: -3px; line-height: 1; }
  .tag { margin-top: 36px; font-size: 46px; line-height: 1.25; color: #e9e3ff; max-width: 1010px; }
  .sub { margin-top: 28px; font-size: 30px; color: #a78bfa; }
  .host { position: absolute; right: 96px; bottom: 56px; font-size: 30px; color: #8b8aa0; }
</style></head><body>
  <div class="bar"></div>
  <div class="brand">TattooOS</div>
  <div class="tag">${TAGLINE}</div>
  <div class="sub">${SUBLINE}</div>
  <div class="host">tattooos.co</div>
</body></html>`;

const here = dirname(fileURLToPath(import.meta.url));
const out = join(here, "..", "public", "og-image.png");

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 630 }, deviceScaleFactor: 1 });
await page.setContent(html);
await page.screenshot({ path: out, type: "png", clip: { x: 0, y: 0, width: 1200, height: 630 } });
await browser.close();
console.log(`wrote ${out}`);
