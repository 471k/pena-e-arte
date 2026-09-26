/**
 * Post-build step: writes one HTML file per static public route (dist/pricing.html,
 * dist/use/booking.html, ...) whose <head> carries that route's own title, description,
 * canonical and OG/Twitter tags, so link-preview bots and search engines see distinct
 * metadata in the raw HTML instead of the shared default in index.html.
 *
 * The React app itself is unchanged: each file is dist/index.html with only the marked head
 * region replaced, so the SPA boots and hydrates exactly as before. nginx serves the file via
 * `try_files $uri $uri.html ...` (frontend/nginx.conf.template).
 *
 * Run with `node scripts/prerender-meta.ts [distDir]` (Node 22.6+ runs .ts directly).
 * Exits non-zero if any route's file could not be written.
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { renderRouteHtml } from "../src/shared/seo/renderRouteHtml.ts";
import { STATIC_ROUTES } from "../src/shared/seo/siteRoutes.ts";

const distDir = resolve(process.argv[2] ?? "dist");
const templatePath = join(distDir, "index.html");

if (!existsSync(templatePath)) {
  console.error(`prerender-meta: ${templatePath} not found — run the Vite build first`);
  process.exit(1);
}

const template = readFileSync(templatePath, "utf8");
let failures = 0;

for (const route of STATIC_ROUTES) {
  try {
    const target = join(distDir, `${route.path.slice(1)}.html`);
    mkdirSync(dirname(target), { recursive: true });
    writeFileSync(target, renderRouteHtml(template, route), "utf8");
    if (!existsSync(target)) throw new Error("file was not written");
    console.log(`prerender-meta: wrote ${route.path.slice(1)}.html`);
  } catch (error) {
    failures += 1;
    console.error(`prerender-meta: ${route.path} failed —`, error instanceof Error ? error.message : error);
  }
}

if (failures > 0) process.exit(1);
